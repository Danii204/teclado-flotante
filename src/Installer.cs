using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TecladoFlotante
{
    /// <summary>Instalación por usuario (sin administrador): AppData, Menú Inicio, arranque y desinstalador.</summary>
    public static class Installer
    {
        public const string AppName = "Teclado Flotante";
        const string RegName = "TecladoFlotante";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TecladoFlotante";
        public const string QuitEventName = @"Local\TecladoFlotante.Quit";
        public const string ShowEventName = @"Local\TecladoFlotante.Show";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TecladoFlotante"); }
        }

        public static string InstalledExe { get { return Path.Combine(InstallDir, "TecladoFlotante.exe"); } }

        static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); }
        }

        static string Version { get { return BuildInfo.DisplayVersion; } }

        /// <param name="silent">Sin diálogos.</param>
        /// <param name="relaunch">Tras instalar, abrir la app (siempre en instalación normal; en actualización automática, también).</param>
        /// <param name="relaunchHidden">Abrirla oculta en la bandeja (si estaba oculta antes de actualizar).</param>
        public static void Install(bool silent, bool relaunch, bool relaunchHidden)
        {
            if (!silent && MessageBox.Show(
                    "Se instalará " + AppName + " para tu usuario.\n\n" +
                    "• Se iniciará automáticamente (oculto) al encender el PC.\n" +
                    "• Muestra u oculta el teclado con Ctrl+Alt+K o desde el icono de la bandeja.\n\n¿Continuar?",
                    AppName + " " + Version, MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return;

            try
            {
                StopRunning();
                Directory.CreateDirectory(InstallDir);
                string self = Path.GetFullPath(Application.ExecutablePath);
                if (!string.Equals(self, InstalledExe, StringComparison.OrdinalIgnoreCase))
                    ReplaceFile(self, InstalledExe);
                CleanupOldExecutables();

                // En una actualización se respeta si el usuario desactivó el inicio con Windows
                bool isUpdate;
                using (RegistryKey existing = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    isUpdate = silent && relaunch && existing != null;
                if (!isUpdate || IsAutostart()) SetAutostart(InstalledExe, true);
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", AppName);
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", string.IsNullOrEmpty(BuildInfo.Author) ? AppName : BuildInfo.Author);
                    if (!string.IsNullOrEmpty(BuildInfo.ProjectUrl))
                    {
                        k.SetValue("URLInfoAbout", BuildInfo.ProjectUrl);
                        k.SetValue("URLUpdateInfo", BuildInfo.ProjectUrl + "/releases");
                    }
                    k.SetValue("DisplayIcon", InstalledExe);
                    k.SetValue("InstallLocation", InstallDir);
                    k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                    k.SetValue("QuietUninstallString", "\"" + InstalledExe + "\" --uninstall --silent");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024) + 1, RegistryValueKind.DWord);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                }
                CreateShortcut(ShortcutPath, InstalledExe);
            }
            catch (Exception ex)
            {
                Log.Error("Fallo en la instalación", ex);
                if (!silent) MessageBox.Show(
                    "No se pudo instalar.\n\n" +
                    "Cierra el teclado (botón ⋯ → Salir) y vuelve a abrir el instalador. " +
                    "Si sigue fallando, reinicia el equipo e inténtalo de nuevo.\n\n" +
                    "Detalle: " + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.ExitCode = 1;
                return;
            }

            Log.Info("Instalada la versión " + Version);
            if (!silent)
                MessageBox.Show(AppName + " se ha instalado.\n\nAhora se abrirá el teclado. Ctrl+Alt+K lo muestra u oculta.",
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (!silent || relaunch)
                Process.Start(new ProcessStartInfo(InstalledExe, relaunchHidden ? "--hidden --updated" : (silent ? "--updated" : ""))
                {
                    UseShellExecute = false,
                    WorkingDirectory = InstallDir,
                });
        }

        public static void Uninstall(bool silent)
        {
            if (!silent && MessageBox.Show("¿Desinstalar " + AppName + "?", AppName,
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            StopRunning();
            try { Registry.CurrentUser.OpenSubKey(RunKey, true).DeleteValue(RegName, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            try { File.Delete(ShortcutPath); } catch { }
            try { Directory.Delete(Settings.Folder, true); } catch { }

            // El propio .exe está en uso: se borra la carpeta un instante después de salir.
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + InstallDir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
            }
            catch { }

            if (!silent) MessageBox.Show(AppName + " se ha desinstalado.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Cierra la instancia que esté en marcha (para actualizar o desinstalar).</summary>
        static void StopRunning()
        {
            try
            {
                EventWaitHandle quit;
                if (EventWaitHandle.TryOpenExisting(QuitEventName, out quit)) using (quit) quit.Set();
            }
            catch { }

            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName("TecladoFlotante"))
            {
                using (p)
                {
                    if (p.Id == me) continue;
                    try { if (!p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(3000); } }
                    catch (Exception ex) { Log.Error("No se pudo cerrar la versión en marcha (pid " + p.Id + ")", ex); }
                }
            }
        }

        /// <summary>
        /// Sustituye el ejecutable instalado aunque siga bloqueado (versión antigua cerrándose, antivirus
        /// analizándolo...): reintenta y, si no hay manera, renombra el antiguo (Windows permite renombrar un
        /// .exe en uso) y copia el nuevo en su lugar. Los restos se borran en el siguiente arranque.
        /// </summary>
        public static void ReplaceFile(string from, string to)
        {
            bool renamed = false;
            for (int i = 0; ; i++)
            {
                try
                {
                    if (File.Exists(to)) File.SetAttributes(to, FileAttributes.Normal);
                    File.Copy(from, to, true);
                    return;
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException) || i >= 40) throw;
                    if (!renamed && i >= 3 && File.Exists(to))
                    {
                        try
                        {
                            string aside = Path.Combine(Path.GetDirectoryName(to), "TecladoFlotante.old-" + Guid.NewGuid().ToString("N") + ".exe");
                            File.Move(to, aside);
                            renamed = true;
                            Log.Info("El ejecutable anterior estaba bloqueado; se ha apartado como " + Path.GetFileName(aside));
                            continue;
                        }
                        catch (Exception moveError) { Log.Error("No se pudo apartar el ejecutable bloqueado", moveError); }
                    }
                    Thread.Sleep(250);
                }
            }
        }

        /// <summary>Borra los ejecutables antiguos apartados por <see cref="ReplaceFile"/> (si ya no están en uso).</summary>
        public static void CleanupOldExecutables()
        {
            try
            {
                if (!Directory.Exists(InstallDir)) return;
                foreach (string f in Directory.GetFiles(InstallDir, "TecladoFlotante.old-*.exe"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        public static bool IsAutostart()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RegName) != null;
        }

        public static void SetAutostart(string exe, bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RegName, "\"" + exe + "\" --hidden");
                else k.DeleteValue(RegName, false);
            }
        }

        static void CreateShortcut(string lnkPath, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Teclado en pantalla flotante en español" });
                lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target + ",0" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(lnk);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
