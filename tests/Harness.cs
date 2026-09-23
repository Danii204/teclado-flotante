using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using TecladoFlotante;

static class Harness
{
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    const uint LEFTDOWN = 0x2, LEFTUP = 0x4;

    static int failures;

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        // Nunca tocar los ajustes reales del usuario
        string data = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TecladoFlotante-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("TECLADOFLOTANTE_DATA_DIR", data);
        try
        {
            if (args.Length > 0 && args[0] == "render") return Render(args[1]);
            if (args.Length > 0 && args[0] == "unit") return Unit();
            Unit();
            return E2E(); // devuelve el total de fallos acumulado
        }
        finally
        {
            try { System.IO.Directory.Delete(data, true); } catch { }
        }
    }

    /// <summary>Pruebas sin ratón (también se ejecutan en GitHub Actions).</summary>
    static int Unit()
    {
        int before = failures;
        string ini = System.IO.Path.Combine(Settings.Folder, "settings.ini");
        System.IO.Directory.CreateDirectory(Settings.Folder);

        System.IO.File.WriteAllText(ini, "\0\0basura=%%%\nW=abc\nH=-5\nOpacity=900\nLayout=klingon\nX=");
        Settings s = Settings.Load();
        Check("ajustes corruptos -> tamaño por defecto", s.KeyboardSize.IsEmpty, true);
        Check("ajustes corruptos -> opacidad acotada", s.Opacity, 100);
        Check("ajustes corruptos -> distribución por defecto", s.Layout, "es");

        s.KeyboardSize = new Size(900, 320); s.Layout = "us"; s.Numpad = false; s.FnRow = false; s.RememberPosition = true;
        s.Running = true; s.SkippedVersion = "9.9.9";
        s.Save(); s.Save(); // la segunda vez usa File.Replace
        Settings r = Settings.Load();
        Check("guardar/cargar: tamaño", r.KeyboardSize, new Size(900, 320));
        Check("guardar/cargar: distribución", r.Layout, "us");
        Check("guardar/cargar: bloque numérico", r.Numpad, false);
        Check("guardar/cargar: fila de funciones", r.FnRow, false);
        Check("guardar/cargar: recordar posición", r.RememberPosition, true);
        Check("guardar/cargar: cierre inesperado detectado", r.Running, true);
        Check("sin archivos temporales colgando", System.IO.File.Exists(ini + ".tmp"), false);

        System.IO.File.WriteAllText(ini, "X=10\nY=10\nW=924\nH=413\nTopMost=1\n");
        Settings v10 = Settings.Load();
        Check("ajustes de la v1.0 se leen", v10.KeyboardSize, new Size(924, 413));
        Check("ajustes de la v1.0 se detectan", v10.NumpadSaved, false);

        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Rectangle c = Settings.Centered(new Size(800, 300));
        Check("centrado horizontal", Math.Abs((c.Left + c.Width / 2) - (wa.Left + wa.Width / 2)) <= 1, true);
        Check("centrado vertical", Math.Abs((c.Top + c.Height / 2) - (wa.Top + wa.Height / 2)) <= 1, true);
        Rectangle big = Settings.Centered(new Size(wa.Width * 3, wa.Height * 3));
        Check("un tamaño enorme se recorta a la pantalla", wa.Contains(big), true);

        foreach (string id in KeyLayout.Ids)
        {
            LayoutDef l = KeyLayout.Build(id);
            for (int row = 1; row <= 5; row++)
            {
                float width = l.Keys.Where(k => !k.Numpad && k.Row == row).Sum(k => k.W);
                // el Intro ocupa las filas 2 y 3: en la fila 3 falta su anchura
                if (row == 3) width += 1.25f;
                Check("distribución " + id + ": fila " + row + " mide 15 unidades", Math.Abs(width - 15f) < 0.01f, true);
            }
            Check("distribución " + id + ": ids únicos", l.Keys.Select(k => k.Id).Distinct().Count(), l.Keys.Count);
        }
        Check("Unicode de ES correcto (ñ)", KeyLayout.Build("es").Keys.Any(k => k.Id == "ñ"), true);
        Check("EE. UU. sin teclas muertas", KeyLayout.Build("us").DeadKeys.Count, 0);
        return failures - before;
    }

    static int Render(string dir)
    {
        KeyboardForm kb = new KeyboardForm();
        kb.Bounds = new Rectangle(0, 0, 1400, 400);
        IntPtr h = kb.Handle;
        Save(kb, dir + "\\normal.png");
        kb.Press(Find(kb, "ShiftL")); Save(kb, dir + "\\shift.png"); kb.Press(Find(kb, "ShiftL"));
        kb.Press(Find(kb, "AltGr")); Save(kb, dir + "\\altgr.png"); kb.Press(Find(kb, "AltGr"));
        kb.Press(Find(kb, "´")); kb.Press(Find(kb, "Caps")); kb.Press(Find(kb, "Ctrl"));
        Save(kb, dir + "\\accent.png");
        kb = new KeyboardForm();
        kb.Bounds = new Rectangle(0, 0, 1400, 400);
        h = kb.Handle;
        kb.Press(Find(kb, "NumLock")); Save(kb, dir + "\\numlockoff.png"); kb.Press(Find(kb, "NumLock"));
        kb.ShowNumpad = false; Save(kb, dir + "\\nonumpad.png"); kb.ShowNumpad = true;
        kb.ShowFnRow = false; Save(kb, dir + "\\nofn.png"); kb.ShowFnRow = true;
        kb.LayoutId = "us"; Save(kb, dir + "\\us.png");
        kb.Press(Find(kb, "ShiftL")); Save(kb, dir + "\\us-shift.png"); kb.Press(Find(kb, "ShiftL"));
        kb.LayoutId = "es";
        kb.Bounds = new Rectangle(0, 0, 480, 170);
        Save(kb, dir + "\\small.png");
        using (Bitmap b = IconArt.Render(256)) b.Save(dir + "\\icon256.png", ImageFormat.Png);
        using (Bitmap b = IconArt.Render(16)) b.Save(dir + "\\icon16.png", ImageFormat.Png);
        return 0;
    }

    static void Save(KeyboardForm kb, string path)
    {
        using (Bitmap bmp = new Bitmap(kb.ClientSize.Width, kb.ClientSize.Height))
        {
            using (Graphics g = Graphics.FromImage(bmp)) kb.Render(g, new Rectangle(Point.Empty, kb.ClientSize));
            bmp.Save(path, ImageFormat.Png);
        }
    }

    static KeyDef Find(KeyboardForm kb, string id)
    {
        KeyDef k = kb.Keys.FirstOrDefault(x => x.Id == id);
        if (k == null) throw new Exception("tecla no encontrada: " + id);
        return k;
    }

    static void Pump(int ms)
    {
        DateTime end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(5); }
    }

    static void ClickAt(Point p)
    {
        SetCursorPos(p.X, p.Y); Pump(25);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); Pump(35);
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero); Pump(45);
    }

    static void DragFromTo(Point a, Point b)
    {
        SetCursorPos(a.X, a.Y); Pump(40);
        mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); Pump(40);
        for (int i = 1; i <= 10; i++)
        {
            SetCursorPos(a.X + (b.X - a.X) * i / 10, a.Y + (b.Y - a.Y) * i / 10);
            Pump(20);
        }
        mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero); Pump(80);
    }

    static void Tap(KeyboardForm kb, params string[] ids)
    {
        foreach (string id in ids)
        {
            RectangleF r = Find(kb, id).Rect;
            ClickAt(kb.PointToScreen(new Point((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2))));
        }
    }

    static void Check(string name, object actual, object expected)
    {
        bool ok = Equals(actual, expected);
        if (!ok) failures++;
        Console.WriteLine((ok ? "OK    " : "FALLO ") + name + (ok ? "" : "  -> obtenido [" + actual + "] esperado [" + expected + "]"));
    }

    static int E2E()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Form target = new Form { Text = "Destino de prueba", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(wa.Left + 40, wa.Top + 40, 700, 140) };
        TextBox box = new TextBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16) };
        target.Controls.Add(box);
        target.Show(); target.Activate(); box.Focus();
        Pump(300);
        // Un clic real da el primer plano aunque Windows bloquee SetForegroundWindow
        ClickAt(box.PointToScreen(new Point(box.Width / 2, box.Height / 2)));
        Pump(200);

        KeyboardForm kb = new KeyboardForm();
        kb.Bounds = new Rectangle(wa.Left + 40, wa.Top + 200, 1250, 360);
        bool minimized = false;
        kb.MinimizeRequested += delegate { minimized = true; kb.HideKeyboard(); };
        kb.ShowKeyboard();
        Pump(300);
        Check("el destino conserva el foco al mostrar el teclado", GetForegroundWindow(), target.Handle);

        Tap(kb, "ShiftL", "h", "o", "l", "a", ",", "Space", "ShiftL", "¡", "q", "u", "´", "e", "Space", "t", "a", "l",
                "ShiftL", "'", "Space", "ShiftL", "ñ", "ñ", "Space", "AltGr", "2", "AltGr", "e", "Space", "ShiftL", "´", "u");
        Check("texto con mayúsculas, acentos, ñ, AltGr y diéresis", box.Text, "Hola, ¿qué tal? Ññ @€ ü");
        Check("el foco sigue en el destino tras escribir", GetForegroundWindow(), target.Handle);

        Tap(kb, "Back");
        Check("Borrar", box.Text, "Hola, ¿qué tal? Ññ @€ ");
        Tap(kb, "Left", "x", "Caps", "a", "Caps", "b");
        Check("flecha izquierda + Bloq Mayús", box.Text, "Hola, ¿qué tal? Ññ @€xAb ");
        Tap(kb, "`", "a", "ShiftL", "`", "o", "´", "Space");
        Check("acento grave, circunflejo y tilde suelta", box.Text, "Hola, ¿qué tal? Ññ @€xAbàô´ ");
        Tap(kb, "Ctrl", "a", "z");
        Check("Ctrl+A (atajo real) y sustitución", box.Text, "z");
        Tap(kb, "num7", "num+", "num3", "num.", "num5", "num/", "num*", "num-");
        Check("bloque numérico", box.Text, "z7+3.5/*-");
        Tap(kb, "NumLock", "num4", "NumLock", "num1");
        Check("Bloq Num desactivado: 4 = flecha izquierda", box.Text, "z7+3.5/*1-");

        kb.LayoutId = "us"; Pump(100);
        Tap(kb, "ShiftL", "2", "`", "a", "\\", "ShiftL", "'");
        Check("distribución English (US): @ ` sin tecla muerta, \\ y \"", box.Text, "z7+3.5/*1@`a\\\"-");
        kb.LayoutId = "es"; Pump(100);
        Tap(kb, "ñ");
        Check("vuelta a Español: ñ", box.Text, "z7+3.5/*1@`a\\\"ñ-");

        float keyH = Find(kb, "a").Rect.Height;
        int hFn = kb.Height;
        kb.ShowFnRow = false; Pump(100);
        Check("sin fila de funciones la ventana es más baja", kb.Height < hFn, true);
        Check("sin fila de funciones las teclas mantienen su alto", Math.Abs(Find(kb, "a").Rect.Height - keyH) <= keyH * 0.04f, true);
        Check("F1 oculta", Find(kb, "F1").Rect.IsEmpty, true);
        kb.ShowFnRow = true; Pump(100);
        Check("fila de funciones restaurada", Math.Abs(kb.Height - hFn) <= 2, true);

        int wNum = kb.Width;
        kb.ShowNumpad = false; Pump(100);
        Check("ocultar bloque numérico estrecha la ventana", Math.Abs(kb.Width - (int)Math.Round((wNum - 8) * 15 / 19.3 + 8)) <= 2, true);
        Check("teclas del bloque numérico ocultas", Find(kb, "num7").Rect.IsEmpty, true);
        kb.ShowNumpad = true; Pump(100);
        Check("volver a mostrarlo recupera el ancho", Math.Abs(kb.Width - wNum) <= 2, true);

        Point loc = kb.Location;
        Point grip = kb.PointToScreen(new Point(kb.ClientSize.Width / 2, 10));
        DragFromTo(grip, new Point(grip.X + 60, grip.Y + 40));
        Check("mover arrastrando la barra", kb.Location, new Point(loc.X + 60, loc.Y + 40));

        int w = kb.Width, hgt = kb.Height;
        Point right = kb.PointToScreen(new Point(kb.ClientSize.Width - 2, kb.ClientSize.Height / 2));
        DragFromTo(right, new Point(right.X - 200, right.Y));
        Check("redimensionar desde el borde derecho", kb.Width, w - 200);
        Point corner = kb.PointToScreen(new Point(kb.ClientSize.Width - 2, kb.ClientSize.Height - 2));
        DragFromTo(corner, new Point(corner.X + 120, corner.Y + 60));
        Check("redimensionar desde la esquina (alto)", kb.Height, hgt + 60);

        Point top = kb.Location;
        Point grip2 = kb.PointToScreen(new Point(kb.ClientSize.Width / 3, 10));
        DragFromTo(grip2, new Point(grip2.X, grip2.Y - (top.Y - wa.Top)));
        Check("se puede colocar arriba del todo", kb.Top, wa.Top);
        Check("el foco sigue en el destino tras mover/redimensionar", GetForegroundWindow(), target.Handle);

        Point minBtn = kb.PointToScreen(new Point(kb.ClientSize.Width - 20, 12));
        ClickAt(minBtn);
        Check("botón minimizar", minimized, true);
        Check("teclado oculto", kb.Visible, false);

        Console.WriteLine(failures == 0 ? "TODAS LAS PRUEBAS OK" : failures + " FALLOS");
        return failures;
    }
}
