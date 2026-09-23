using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TecladoFlotante
{
    /// <summary>Contexto principal: teclado, botón flotante, icono de bandeja, atajo global, actualizaciones y señales entre instancias.</summary>
    public class TrayApp : ApplicationContext
    {
        const int HotkeyId = 1;
        const int FirstUpdateCheckMs = 90 * 1000;
        const int UpdateCheckIntervalMs = 12 * 60 * 60 * 1000;

        readonly Settings settings;
        readonly KeyboardForm kb;
        readonly BubbleForm bubble;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly EventWaitHandle showEvent, quitEvent;
        readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer();
        readonly AutoShow autoShow;
        ToolStripMenuItem miTheme, miAutoShow;
        ToolStripMenuItem miUpdate, miToggle, miTopMost, miNumpad, miFnRow, miBubble, miRemember, miAutostart, miAutoUpdate, miOpacity, miLayout, miCheckNow;
        ReleaseInfo availableUpdate;
        bool updating, hotkeyOk, exiting;

        public TrayApp(bool startHidden, bool recovered, bool updated)
        {
            settings = Settings.Load();
            bool crashed = settings.Running;
            if (crashed) Log.Info("La sesión anterior no se cerró correctamente");
            settings.Running = true;
            Updater.CleanupDownloads();
            Theme.Apply(settings.ThemeMode);

            kb = new KeyboardForm();
            kb.ApplyTheme();
            MigrateFromV10();
            kb.Configure(settings.Layout, settings.Numpad, settings.FnRow);
            kb.Bounds = StartBounds(crashed || recovered);
            kb.KeyboardTopMost = settings.TopMost;
            kb.Opacity = settings.Opacity / 100.0;
            IntPtr h = kb.Handle; // crea la ventana (oculta) para poder recibir el atajo global
            hotkeyOk = Native.RegisterHotKey(h, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.K);
            if (!hotkeyOk) Log.Info("Ctrl+Alt+K está ocupado por otro programa");
            SaveState();

            kb.HotkeyPressed += delegate { Toggle(); };
            kb.MinimizeRequested += delegate { HideKeyboard(); };
            kb.TopMostToggled += delegate { SaveState(); };
            kb.BoundsCommitted += delegate { SaveState(); };
            kb.MenuRequested += (s, e) => menu.Show(e.Point);

            bubble = new BubbleForm();
            bubble.Clicked += delegate { ShowKeyboard(); };
            bubble.Moved += delegate { settings.BubblePos = bubble.Location; settings.Save(); };
            bubble.MenuRequested += (s, e) => menu.Show(e.Point);

            autoShow = new AutoShow(kb,
                p => (kb.Visible && kb.Bounds.Contains(p)) || (bubble.Visible && bubble.Bounds.Contains(p)),
                delegate { if (!kb.Visible && settings.AutoShow) ShowKeyboard(); });
            if (settings.AutoShow) autoShow.Start();

            menu = BuildMenu();
            tray = new NotifyIcon();
            tray.Icon = IconArt.CreateIcon(SystemInformation.SmallIconSize.Width);
            tray.Text = "Teclado Flotante " + BuildInfo.DisplayVersion + (hotkeyOk ? " (Ctrl+Alt+K)" : "");
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Toggle(); };
            tray.BalloonTipClicked += delegate { if (availableUpdate != null) PromptUpdate(); };
            tray.Visible = true;

            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            SystemEvents.SessionEnded += OnSessionEnded;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ShowEventName);
            quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.QuitEventName);
            Thread t = new Thread(WaitSignals);
            t.IsBackground = true;
            t.Name = "Señales";
            t.Start();

            updateTimer.Tick += delegate { updateTimer.Interval = UpdateCheckIntervalMs; CheckForUpdates(false); };
            updateTimer.Interval = FirstUpdateCheckMs;
            if (Updater.Enabled) updateTimer.Start();

            if (!startHidden) ShowKeyboard();
            if (updated) Balloon("Teclado Flotante actualizado", "Ya tienes la versión " + BuildInfo.DisplayVersion + ".", ToolTipIcon.Info);
            else if (recovered) Balloon("Teclado Flotante", "Se ha vuelto a abrir tras un error inesperado.", ToolTipIcon.Warning);
            else if (!hotkeyOk && !startHidden)
                Balloon("Atajo no disponible", "Otro programa usa Ctrl+Alt+K. Usa el icono de la bandeja para mostrar el teclado.", ToolTipIcon.Warning);
        }

        // ------------------------------------------------------------------ posición y estado

        /// <summary>
        /// Al arrancar se conserva el tamaño guardado y el teclado aparece centrado. Solo se recuerda la
        /// posición si el usuario lo activó y la sesión anterior terminó bien.
        /// </summary>
        Rectangle StartBounds(bool afterFailure)
        {
            Size size = settings.KeyboardSize.IsEmpty ? Settings.DefaultKeyboardSize(settings.Numpad, settings.FnRow) : settings.KeyboardSize;
            if (settings.RememberPosition && !afterFailure && settings.KeyboardPos.X != int.MinValue)
            {
                Rectangle r = new Rectangle(settings.KeyboardPos, size);
                if (Settings.IsVisibleOnScreen(r, 80)) return r;
            }
            return Settings.Centered(size);
        }

        /// <summary>Ajustes de la versión 1.0 (sin bloque numérico): ensanchar para que las teclas no encojan.</summary>
        void MigrateFromV10()
        {
            if (settings.NumpadSaved || settings.KeyboardSize.IsEmpty) return;
            Size s = settings.KeyboardSize;
            settings.KeyboardSize = new Size((int)(s.Width * KeyLayout.Columns(true) / KeyLayout.MainColumns), s.Height);
        }

        void SaveState()
        {
            settings.KeyboardSize = kb.Size;
            settings.KeyboardPos = kb.Location;
            settings.TopMost = kb.KeyboardTopMost;
            settings.Opacity = (int)Math.Round(kb.Opacity * 100);
            settings.Numpad = kb.ShowNumpad;
            settings.FnRow = kb.ShowFnRow;
            settings.Layout = kb.LayoutId;
            settings.Save();
        }

        void EnsureOnScreen()
        {
            if (!Settings.IsVisibleOnScreen(kb.Bounds, 80))
            {
                kb.Bounds = Settings.Centered(kb.Size);
                SaveState();
            }
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            // Monitor desconectado, cambio de resolución...: que el teclado nunca quede fuera de la vista
            try
            {
                kb.BeginInvoke((MethodInvoker)delegate
                {
                    EnsureOnScreen();
                    if (bubble.Visible && !Settings.IsVisibleOnScreen(bubble.Bounds, bubble.Width / 2)) bubble.ShowAt(bubble.Location);
                });
            }
            catch (InvalidOperationException) { }
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // En modo automático, seguir al tema de Windows en cuanto se cambia
            if (settings.ThemeMode != Theme.Auto || e.Category != UserPreferenceCategory.General) return;
            try { kb.BeginInvoke((MethodInvoker)delegate { SetTheme(Theme.Auto); }); }
            catch (InvalidOperationException) { }
        }

        void SetTheme(string mode)
        {
            settings.ThemeMode = mode;
            Theme.Apply(mode);
            kb.ApplyTheme();
            settings.Save();
        }

        void OnSessionEnded(object sender, SessionEndedEventArgs e)
        {
            // Apagado o cierre de sesión: es un cierre limpio
            settings.Running = false;
            SaveState();
        }

        void WaitSignals()
        {
            WaitHandle[] handles = { showEvent, quitEvent };
            while (true)
            {
                int i = WaitHandle.WaitAny(handles);
                try
                {
                    if (i == 0) kb.BeginInvoke((MethodInvoker)ShowKeyboard);
                    else { kb.BeginInvoke((MethodInvoker)ExitThread); return; }
                }
                catch (InvalidOperationException) { return; }
            }
        }

        // ------------------------------------------------------------------ mostrar / ocultar

        public void ShowKeyboard()
        {
            bubble.Hide();
            EnsureOnScreen();
            kb.ShowKeyboard();
        }

        public void HideKeyboard()
        {
            kb.HideKeyboard();
            if (settings.Bubble)
            {
                Point p = settings.BubblePos;
                if (p.X == int.MinValue) p = new Point(kb.Right - bubble.Width - 8, kb.Top + 8);
                bubble.ShowAt(p);
            }
        }

        public void Toggle()
        {
            if (kb.Visible) HideKeyboard();
            else ShowKeyboard();
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(8000, title, text, icon);
        }

        // ------------------------------------------------------------------ actualizaciones

        void CheckForUpdates(bool manual)
        {
            if (!Updater.Enabled || updating) return;
            if (!manual && !settings.AutoUpdate) return;
            miCheckNow.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r = null;
                Exception error = null;
                try { r = Updater.GetLatest(); }
                catch (Exception ex) { error = ex; }
                try { kb.BeginInvoke((MethodInvoker)delegate { OnUpdateChecked(r, error, manual); }); }
                catch (InvalidOperationException) { }
            });
        }

        void OnUpdateChecked(ReleaseInfo r, Exception error, bool manual)
        {
            miCheckNow.Enabled = true;
            if (error != null)
            {
                Log.Error("No se pudo comprobar si hay actualizaciones", error);
                if (manual) MessageBox.Show("No se pudo comprobar si hay actualizaciones.\nRevisa la conexión a Internet.\n\n" + error.Message,
                    Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Updater.IsNewer(r))
            {
                if (manual) MessageBox.Show("Tienes la última versión (" + BuildInfo.DisplayVersion + ").", Installer.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            availableUpdate = r;
            miUpdate.Text = "Actualizar a la versión " + r.Version;
            miUpdate.Visible = true;
            if (manual) PromptUpdate();
            else if (settings.SkippedVersion != r.Version.ToString())
                Balloon("Nueva versión disponible: " + r.Version, "Haz clic aquí para actualizar (tarda unos segundos).", ToolTipIcon.Info);
        }

        void PromptUpdate()
        {
            ReleaseInfo r = availableUpdate;
            if (r == null || updating) return;
            DialogResult answer = MessageBox.Show(
                "Hay una nueva versión de Teclado Flotante: " + r.Version + " (tienes la " + BuildInfo.DisplayVersion + ").\n\n" +
                "El teclado se cerrará un momento y volverá a abrirse solo.\n\n¿Actualizar ahora?\n\n" +
                "(«No» = recordármelo más tarde · «Cancelar» = no avisar de esta versión)",
                Installer.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { settings.SkippedVersion = r.Version.ToString(); settings.Save(); return; }
            if (answer != DialogResult.Yes) return;

            updating = true;
            miUpdate.Enabled = false;
            miUpdate.Text = "Descargando la versión " + r.Version + "…";
            bool hidden = !kb.Visible;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = null;
                Exception error = null;
                try { path = Updater.Download(r); }
                catch (Exception ex) { error = ex; }
                try { kb.BeginInvoke((MethodInvoker)delegate { OnDownloaded(r, path, error, hidden); }); }
                catch (InvalidOperationException) { }
            });
        }

        void OnDownloaded(ReleaseInfo r, string path, Exception error, bool hidden)
        {
            if (error == null)
            {
                try
                {
                    Log.Info("Actualizando a " + r.Version);
                    SaveState();
                    Updater.Launch(path, hidden); // el instalador cerrará esta instancia y abrirá la nueva
                    return;
                }
                catch (Exception ex) { error = ex; }
            }
            Log.Error("La actualización falló", error);
            updating = false;
            miUpdate.Enabled = true;
            miUpdate.Text = "Actualizar a la versión " + r.Version;
            if (MessageBox.Show("No se pudo actualizar:\n" + error.Message + "\n\n¿Abrir la página de descargas para hacerlo a mano?",
                    Installer.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                OpenUrl(r.PageUrl);
        }

        static void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("No se pudo abrir " + url, ex); }
        }

        // ------------------------------------------------------------------ menú

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();

            miUpdate = new ToolStripMenuItem("Actualizar", null, delegate { PromptUpdate(); });
            miUpdate.Font = new Font(miUpdate.Font, FontStyle.Bold);
            miUpdate.Visible = false;

            miToggle = new ToolStripMenuItem("Mostrar teclado", null, delegate { Toggle(); });
            miToggle.ShortcutKeyDisplayString = "Ctrl+Alt+K";

            miLayout = new ToolStripMenuItem("Distribución");
            foreach (string id in KeyLayout.Ids)
            {
                string layoutId = id;
                ToolStripMenuItem item = new ToolStripMenuItem(KeyLayout.NameOf(id), null, delegate
                {
                    kb.LayoutId = layoutId;
                    SaveState();
                });
                item.Tag = id;
                miLayout.DropDownItems.Add(item);
            }

            miNumpad = new ToolStripMenuItem("Bloque numérico", null, delegate { kb.ShowNumpad = !kb.ShowNumpad; SaveState(); });
            miFnRow = new ToolStripMenuItem("Fila de funciones (F1–F12)", null, delegate { kb.ShowFnRow = !kb.ShowFnRow; SaveState(); });
            miTheme = new ToolStripMenuItem("Tema");
            foreach (string[] option in new[] { new[] { Theme.Auto, "Automático (como Windows)" }, new[] { Theme.Light, "Claro" }, new[] { Theme.Dark, "Oscuro" } })
            {
                string mode = option[0];
                ToolStripMenuItem item = new ToolStripMenuItem(option[1], null, delegate { SetTheme(mode); });
                item.Tag = mode;
                miTheme.DropDownItems.Add(item);
            }

            miAutoShow = new ToolStripMenuItem("Mostrar al tocar un campo de texto", null, delegate
            {
                settings.AutoShow = !settings.AutoShow;
                if (settings.AutoShow) autoShow.Start(); else autoShow.Stop();
                settings.Save();
            });

            miTopMost = new ToolStripMenuItem("Siempre encima de las ventanas", null, delegate
            {
                kb.KeyboardTopMost = !kb.KeyboardTopMost;
                SaveState();
            });

            miOpacity = new ToolStripMenuItem("Opacidad");
            foreach (int pct in new[] { 100, 90, 80, 70, 60, 50, 40 })
            {
                int value = pct;
                ToolStripMenuItem item = new ToolStripMenuItem(pct + " %", null, delegate { kb.Opacity = value / 100.0; SaveState(); });
                item.Tag = pct;
                miOpacity.DropDownItems.Add(item);
            }

            miBubble = new ToolStripMenuItem("Botón flotante al minimizar", null, delegate
            {
                settings.Bubble = !settings.Bubble;
                if (!settings.Bubble) bubble.Hide();
                settings.Save();
            });
            miRemember = new ToolStripMenuItem("Recordar la posición al reiniciar", null, delegate
            {
                settings.RememberPosition = !settings.RememberPosition;
                SaveState();
            });
            miAutostart = new ToolStripMenuItem("Iniciar con Windows (oculto)", null, delegate
            {
                try { Installer.SetAutostart(Application.ExecutablePath, !Installer.IsAutostart()); }
                catch (Exception ex) { Log.Error("No se pudo cambiar el inicio automático", ex); }
            });
            ToolStripMenuItem miReset = new ToolStripMenuItem("Restablecer posición y tamaño", null, delegate
            {
                kb.Bounds = Settings.Centered(Settings.DefaultKeyboardSize(kb.ShowNumpad, kb.ShowFnRow));
                settings.BubblePos = new Point(int.MinValue, int.MinValue);
                SaveState();
                ShowKeyboard();
            });

            ToolStripMenuItem miSettings = new ToolStripMenuItem("Opciones");
            miSettings.DropDownItems.AddRange(new ToolStripItem[]
            {
                miTopMost, miOpacity, miBubble, miRemember, miAutostart, new ToolStripSeparator(), miReset,
            });

            miCheckNow = new ToolStripMenuItem("Buscar actualizaciones ahora", null, delegate { CheckForUpdates(true); });
            miAutoUpdate = new ToolStripMenuItem("Avisar de nuevas versiones", null, delegate
            {
                settings.AutoUpdate = !settings.AutoUpdate;
                settings.Save();
            });
            ToolStripMenuItem miAbout = new ToolStripMenuItem("Acerca de Teclado Flotante…", null, delegate { ShowAbout(); });
            ToolStripMenuItem miHelp = new ToolStripMenuItem("Ayuda y actualizaciones");
            miHelp.DropDownItems.AddRange(new ToolStripItem[] { miCheckNow, miAutoUpdate, new ToolStripSeparator(), miAbout });
            if (!Updater.Enabled) { miCheckNow.Visible = false; miAutoUpdate.Visible = false; }

            ToolStripMenuItem miExit = new ToolStripMenuItem("Salir", null, delegate { ExitThread(); });

            m.Items.AddRange(new ToolStripItem[]
            {
                miUpdate, miToggle, new ToolStripSeparator(),
                miLayout, miNumpad, miFnRow, miTheme, miAutoShow, miSettings, miHelp,
                new ToolStripSeparator(), miExit,
            });

            m.Opening += delegate
            {
                miToggle.Text = kb.Visible ? "Ocultar teclado" : "Mostrar teclado";
                miToggle.ShortcutKeyDisplayString = hotkeyOk ? "Ctrl+Alt+K" : "";
                foreach (ToolStripMenuItem i in miLayout.DropDownItems) i.Checked = (string)i.Tag == kb.LayoutId;
                miNumpad.Checked = kb.ShowNumpad;
                miFnRow.Checked = kb.ShowFnRow;
                foreach (ToolStripMenuItem i in miTheme.DropDownItems) i.Checked = (string)i.Tag == settings.ThemeMode;
                miAutoShow.Checked = settings.AutoShow && autoShow.Enabled;
                miTopMost.Checked = kb.KeyboardTopMost;
                miBubble.Checked = settings.Bubble;
                miRemember.Checked = settings.RememberPosition;
                miAutoUpdate.Checked = settings.AutoUpdate;
                try { miAutostart.Checked = Installer.IsAutostart(); } catch { miAutostart.Checked = false; }
                int current = (int)Math.Round(kb.Opacity * 100);
                foreach (ToolStripMenuItem i in miOpacity.DropDownItems) i.Checked = Math.Abs((int)i.Tag - current) < 5;
            };
            return m;
        }

        void ShowAbout()
        {
            string text = "Teclado Flotante " + BuildInfo.DisplayVersion + "\n" +
                          "Teclado en pantalla flotante para Windows.\n\n" +
                          (BuildInfo.IsBeta && BuildInfo.ProjectUrl.Length > 0
                              ? "Beta abierta: si encuentras un fallo, cuéntalo en " + BuildInfo.ProjectUrl + "/issues\n\n" : "") +
                          (string.IsNullOrEmpty(BuildInfo.Author) ? "" : "Autor: " + BuildInfo.Author + "\n") +
                          (string.IsNullOrEmpty(BuildInfo.ProjectUrl) ? "" : BuildInfo.ProjectUrl + "\n") +
                          "\nNo recopila ni envía ningún dato. Solo consulta GitHub para buscar nuevas versiones.\n" +
                          "Registro de errores (local): " + Log.FilePath;
            if (string.IsNullOrEmpty(BuildInfo.ProjectUrl))
                MessageBox.Show(text, Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (MessageBox.Show(text + "\n\n¿Abrir la página del proyecto?", Installer.AppName,
                         MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                OpenUrl(BuildInfo.ProjectUrl);
        }

        // ------------------------------------------------------------------ salida

        protected override void ExitThreadCore()
        {
            if (exiting) return;
            exiting = true;
            try
            {
                updateTimer.Stop();
                SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
                SystemEvents.SessionEnded -= OnSessionEnded;
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                autoShow.Dispose();
                settings.Running = false;
                SaveState();
                Native.UnregisterHotKey(kb.Handle, HotkeyId);
                tray.Visible = false;
                tray.Dispose();
                bubble.Dispose();
                kb.Dispose();
                Log.Info("Salida normal");
            }
            catch (Exception ex) { Log.Error("Error al salir", ex); }
            base.ExitThreadCore();
        }
    }
}
