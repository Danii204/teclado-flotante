using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TecladoFlotante
{
    /// <summary>
    /// Ajustes en %APPDATA%\TecladoFlotante\settings.ini. Cualquier valor ausente, corrupto o fuera de rango
    /// se sustituye por el predeterminado: un archivo dañado nunca impide arrancar.
    /// </summary>
    public class Settings
    {
        public Size KeyboardSize = Size.Empty;       // tamaño guardado (se conserva siempre)
        public Point KeyboardPos = new Point(int.MinValue, int.MinValue);
        public bool RememberPosition;                 // si no, al arrancar aparece centrado
        public bool TopMost = true;
        public int Opacity = 100;
        public bool Bubble = true;
        public Point BubblePos = new Point(int.MinValue, int.MinValue);
        public bool Numpad = true;
        public bool FnRow = true;
        public string Layout = KeyLayout.DefaultId;
        public bool AutoUpdate = true;
        public string ThemeMode = TecladoFlotante.Theme.Auto;   // auto (como Windows) / light / dark
        public bool AutoShow;                                    // mostrar al tocar un campo de texto
        public string SkippedVersion = "";
        public bool Running;                          // true mientras se ejecuta: si sigue así al arrancar, hubo un cierre inesperado

        /// <summary>False si el archivo es de la versión 1.0 (sin bloque numérico).</summary>
        public bool NumpadSaved;

        public static string Folder
        {
            get
            {
                // Las pruebas automáticas usan una carpeta aparte para no tocar los ajustes reales
                string custom = Environment.GetEnvironmentVariable("TECLADOFLOTANTE_DATA_DIR");
                if (!string.IsNullOrEmpty(custom)) return custom;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TecladoFlotante");
            }
        }

        static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch (Exception ex)
            {
                Log.Error("No se pudieron leer los ajustes; se usan los predeterminados", ex);
                return s;
            }

            int w = Int(d, "W", 0), h = Int(d, "H", 0);
            if (w >= 200 && h >= 80 && w <= 20000 && h <= 20000) s.KeyboardSize = new Size(w, h);
            s.KeyboardPos = new Point(Int(d, "X", int.MinValue), Int(d, "Y", int.MinValue));
            s.RememberPosition = Int(d, "RememberPosition", 0) != 0;
            s.TopMost = Int(d, "TopMost", 1) != 0;
            s.Opacity = Math.Max(30, Math.Min(100, Int(d, "Opacity", 100)));
            s.Bubble = Int(d, "Bubble", 1) != 0;
            s.BubblePos = new Point(Int(d, "BubbleX", int.MinValue), Int(d, "BubbleY", int.MinValue));
            s.NumpadSaved = d.ContainsKey("Numpad");
            s.Numpad = Int(d, "Numpad", 1) != 0;
            s.FnRow = Int(d, "FnRow", 1) != 0;
            string layout;
            if (d.TryGetValue("Layout", out layout) && Array.IndexOf(KeyLayout.Ids, layout) >= 0) s.Layout = layout;
            s.AutoUpdate = Int(d, "AutoUpdate", 1) != 0;
            string theme;
            if (d.TryGetValue("Theme", out theme) && TecladoFlotante.Theme.IsValidMode(theme)) s.ThemeMode = theme;
            s.AutoShow = Int(d, "AutoShow", 0) != 0;
            string skipped;
            if (d.TryGetValue("SkippedVersion", out skipped)) s.SkippedVersion = skipped;
            s.Running = Int(d, "Running", 0) != 0;
            return s;
        }

        /// <summary>Escritura atómica: se escribe un temporal y se sustituye, así un apagado a mitad no corrompe nada.</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string[] lines =
                {
                    "; Teclado Flotante " + BuildInfo.Version,
                    "W=" + KeyboardSize.Width, "H=" + KeyboardSize.Height,
                    "X=" + KeyboardPos.X, "Y=" + KeyboardPos.Y,
                    "RememberPosition=" + B(RememberPosition),
                    "TopMost=" + B(TopMost),
                    "Opacity=" + Opacity,
                    "Bubble=" + B(Bubble),
                    "BubbleX=" + BubblePos.X, "BubbleY=" + BubblePos.Y,
                    "Numpad=" + B(Numpad),
                    "FnRow=" + B(FnRow),
                    "Layout=" + Layout,
                    "AutoUpdate=" + B(AutoUpdate),
                    "Theme=" + ThemeMode,
                    "AutoShow=" + B(AutoShow),
                    "SkippedVersion=" + SkippedVersion,
                    "Running=" + B(Running),
                };
                string tmp = FilePath + ".tmp";
                File.WriteAllLines(tmp, lines, new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null, true);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Log.Error("No se pudieron guardar los ajustes", ex);
            }
        }

        static string B(bool b) { return b ? "1" : "0"; }

        static int Int(Dictionary<string, string> d, string key, int def)
        {
            string v;
            int r;
            return d.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        /// <summary>True si al menos un trozo razonable del rectángulo cae dentro de alguna pantalla.</summary>
        public static bool IsVisibleOnScreen(Rectangle r, int minOverlap)
        {
            foreach (Screen sc in Screen.AllScreens)
            {
                Rectangle i = Rectangle.Intersect(sc.WorkingArea, r);
                if (i.Width >= minOverlap && i.Height >= minOverlap) return true;
            }
            return false;
        }

        public static Size DefaultKeyboardSize(bool numpad, bool fnRow)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            float cols = KeyLayout.Columns(numpad);
            float widthFactor = cols / KeyLayout.MainColumns;
            int w = (int)Math.Min(wa.Width * 0.62 * widthFactor, 1250 * widthFactor);
            w = Math.Min(w, wa.Width - 24);
            int h = (int)(w * 0.36 * KeyLayout.MainColumns / cols * KeyLayout.Rows(fnRow) / KeyLayout.Rows(true));
            return new Size(w, h);
        }

        /// <summary>Centrado en la pantalla principal, recortando el tamaño si no cabe.</summary>
        public static Rectangle Centered(Size size)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(size.Width, wa.Width), h = Math.Min(size.Height, wa.Height);
            return new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, w, h);
        }
    }
}
