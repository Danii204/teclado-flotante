using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TecladoFlotante
{
    /// <summary>
    /// Argumentos:
    ///   (ninguno)       abre el teclado (o lo muestra si ya estaba en marcha)
    ///   --hidden        arranca oculto en la bandeja (inicio con Windows)
    ///   --install       instala (también si el ejecutable se llama *Setup*.exe)
    ///   --update        instalación silenciosa lanzada por el actualizador; vuelve a abrir la app
    ///   --uninstall     desinstala
    ///   --silent        sin diálogos (instalar / desinstalar)
    ///   --check-update  busca e instala una actualización sin preguntar (soporte / pruebas)
    /// </summary>
    public static class Program
    {
        const string MutexName = @"Local\TecladoFlotante.Instance";
        static bool uiMode;

        [STAThread]
        public static int Main(string[] args)
        {
            Func<string, bool> has = a => args.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase));

            int iconArg = Array.FindIndex(args, x => x == "--write-icon");
            if (iconArg >= 0 && iconArg + 1 < args.Length) { IconArt.WriteIcoFile(args[iconArg + 1]); return 0; }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Error("Error no controlado en la interfaz (se continúa)", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += OnFatalError;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = has("--silent");
            if (has("--uninstall")) { Installer.Uninstall(silent); return 0; }

            string exeName = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
            if (has("--update"))
            {
                Installer.Install(true, true, has("--hidden"));
                return Environment.ExitCode;
            }
            if (has("--install") || exeName.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Installer.Install(silent, false, false);
                return Environment.ExitCode;
            }
            if (has("--check-update")) return CheckUpdateNow();

            bool recovered = has("--recovered");
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created && recovered)
                {
                    // Relanzado tras un fallo: esperar a que el proceso anterior termine de cerrarse
                    try { created = mutex.WaitOne(10000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    // Ya está en marcha (p. ej. oculto en la bandeja): pedirle que se muestre.
                    if (!has("--hidden"))
                    {
                        EventWaitHandle show;
                        if (EventWaitHandle.TryOpenExisting(Installer.ShowEventName, out show)) using (show) show.Set();
                    }
                    return 0;
                }

                uiMode = true;
                Log.Info("Inicio" + (has("--hidden") ? " (oculto)" : "") + (recovered ? " tras un fallo" : "") + (has("--updated") ? " tras actualizar" : ""));
                Application.Run(new TrayApp(has("--hidden") && !recovered, recovered, has("--updated")));
                mutex.ReleaseMutex();
            }
            return 0;
        }

        /// <summary>
        /// Fallo irrecuperable: se registra y, si la app llevaba un rato funcionando, se vuelve a abrir sola
        /// (centrada). Si falla nada más arrancar no se relanza, para no entrar en un bucle.
        /// </summary>
        static void OnFatalError(object sender, UnhandledExceptionEventArgs e)
        {
            Log.Error("Error fatal", e.ExceptionObject as Exception);
            if (!uiMode) return;
            try
            {
                TimeSpan uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                if (uptime.TotalSeconds > 30)
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--recovered") { UseShellExecute = false });
            }
            catch { }
        }

        static int CheckUpdateNow()
        {
            try
            {
                ReleaseInfo r = Updater.GetLatest();
                if (!Updater.IsNewer(r)) { Log.Info("--check-update: no hay versión más nueva"); return 0; }
                Updater.Launch(Updater.Download(r), true);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("--check-update falló", ex);
                return 1;
            }
        }
    }
}
