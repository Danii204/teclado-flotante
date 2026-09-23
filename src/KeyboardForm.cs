using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace TecladoFlotante
{
    public class ScreenPointEventArgs : EventArgs
    {
        public Point Point;
        public ScreenPointEventArgs(Point p) { Point = p; }
    }

    /// <summary>
    /// Ventana del teclado. Nunca se activa (WS_EX_NOACTIVATE) para que el foco se quede en la
    /// aplicación donde se escribe. Todo se pinta a mano: sin controles hijos, redimensionar es fluido.
    /// </summary>
    public class KeyboardForm : Form
    {
        public event EventHandler MinimizeRequested;
        public event EventHandler TopMostToggled;
        public event EventHandler HotkeyPressed;
        public event EventHandler BoundsCommitted;
        public event EventHandler<ScreenPointEventArgs> MenuRequested;
        public event EventHandler UpdateRequested;

        enum Button { None = -1, Menu = 0, Pin = 1, Minimize = 2 }
        enum Drag { None, Move, Resize }
        const int EdgeL = 1, EdgeR = 2, EdgeT = 4, EdgeB = 8;

        LayoutDef layout = KeyLayout.Build(KeyLayout.DefaultId);
        public List<KeyDef> Keys { get { return layout.Keys; } }
        readonly FontCache fonts = new FontCache();
        readonly Timer repeatTimer = new Timer();
        readonly StringFormat center = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.None,
        };

        bool topMost = true;
        float scale = 1f;

        // Estado del teclado
        bool shift, caps, ctrl, alt, altGr, win;
        bool numLock = true;
        bool showNumpad = true;
        bool showFnRow = true;
        bool boldText;
        string pending; // tecla muerta pendiente (´ ` ^ ¨)

        // Geometría
        RectangleF strip;
        float contentW, contentH;
        RectangleF updateButton;        // botón «Actualizar» de la barra (vacío si no hay versión nueva)
        string updateLabel;
        bool updatePressed;
        readonly RectangleF[] buttons = new RectangleF[3];
        float unitH;
        int pad, edge;

        // Interacción
        KeyDef pressed, hover;
        Button pressedButton = Button.None, hoverButton = Button.None;
        Drag drag = Drag.None;
        int dragEdges;
        Point dragStart;
        Rectangle dragBounds;

        public KeyboardForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            Text = "Teclado Flotante";
            BackColor = Theme.Back;
            MinimumSize = new Size(380, 150);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
            repeatTimer.Tick += RepeatTick;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                if (topMost) cp.ExStyle |= Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public bool KeyboardTopMost
        {
            get { return topMost; }
            set
            {
                topMost = value;
                if (IsHandleCreated)
                    Native.SetWindowPos(Handle, value ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                Invalidate();
            }
        }

        /// <summary>Texto del botón de actualización de la barra superior; null lo oculta.</summary>
        public string UpdateLabel
        {
            get { return updateLabel; }
            set
            {
                updateLabel = value;
                DoLayout();
                Invalidate();
            }
        }

        /// <summary>Letras de las teclas en negrita (se leen mejor).</summary>
        public bool BoldText
        {
            get { return boldText; }
            set { boldText = value; Invalidate(); }
        }

        FontStyle TextStyle { get { return boldText ? FontStyle.Bold : FontStyle.Regular; } }

        public string LayoutId
        {
            get { return layout.Id; }
            set { Configure(value, showNumpad, showFnRow); }
        }

        public bool ShowNumpad
        {
            get { return showNumpad; }
            set { Configure(layout.Id, value, showFnRow); }
        }

        public bool ShowFnRow
        {
            get { return showFnRow; }
            set { Configure(layout.Id, showNumpad, value); }
        }

        /// <summary>
        /// Cambia la distribución y los bloques visibles. Si aparece o desaparece un bloque, la ventana
        /// crece o encoge para que las teclas conserven su tamaño (sin salirse de la pantalla).
        /// </summary>
        public void Configure(string layoutId, bool numpad, bool fnRow)
        {
            bool resize = IsHandleCreated && contentW > 0 && (numpad != showNumpad || fnRow != showFnRow);
            float fx = KeyLayout.Columns(numpad) / KeyLayout.Columns(showNumpad);
            float fy = KeyLayout.Rows(fnRow) / KeyLayout.Rows(showFnRow);
            float cw = contentW, ch = contentH;

            if (layoutId != layout.Id)
            {
                EndInteraction();
                layout = KeyLayout.Build(layoutId);
                hover = null;
                pending = null;
            }
            showNumpad = numpad;
            showFnRow = fnRow;

            if (resize)
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                int w = Width - (int)Math.Round(cw) + (int)Math.Round(cw * fx);
                int h = Height - (int)Math.Round(ch) + (int)Math.Round(ch * fy);
                w = Math.Max(MinimumSize.Width, Math.Min(w, wa.Width));
                h = Math.Max(MinimumSize.Height, Math.Min(h, wa.Height));
                int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - w));
                int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - h));
                Bounds = new Rectangle(x, y, w, h);
            }
            DoLayout();
            Invalidate();
            if (resize && BoundsCommitted != null) BoundsCommitted(this, EventArgs.Empty);
        }

        /// <summary>Muestra sin robar el foco y lo pone delante de las demás ventanas.</summary>
        public void ShowKeyboard()
        {
            if (!Visible) Show();
            Native.SetWindowPos(Handle, topMost ? Native.HWND_TOPMOST : Native.HWND_TOP, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void HideKeyboard()
        {
            EndInteraction();
            Hide();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            scale = Native.DpiScale(Handle);
            Native.RoundCorners(Handle, 2, ColorBgr(Theme.Border));
            DoLayout();
        }

        static int ColorBgr(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        /// <summary>Repinta con los colores actuales de <see cref="Theme"/> (tras cambiar de tema).</summary>
        public void ApplyTheme()
        {
            BackColor = Theme.Back;
            if (IsHandleCreated) Native.RoundCorners(Handle, 2, ColorBgr(Theme.Border));
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE:
                    m.Result = (IntPtr)Native.MA_NOACTIVATE;
                    return;
                case Native.WM_HOTKEY:
                    if (HotkeyPressed != null) HotkeyPressed(this, EventArgs.Empty);
                    return;
                case Native.WM_DPICHANGED:
                    scale = (m.WParam.ToInt32() & 0xFFFF) / 96f;
                    Native.RECT r = (Native.RECT)System.Runtime.InteropServices.Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                    if (drag == Drag.None)
                        Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    DoLayout();
                    Invalidate();
                    return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ geometría

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        void DoLayout()
        {
            Size cs = ClientSize;
            if (cs.Width <= 0 || cs.Height <= 0) return;
            pad = (int)Math.Round(4 * scale);
            edge = (int)Math.Round(7 * scale);

            float stripH = Math.Max(16 * scale, Math.Min(30 * scale, cs.Height * 0.075f));
            strip = new RectangleF(pad, pad, cs.Width - 2 * pad, stripH);
            float bw = stripH * 1.6f;
            for (int i = 0; i < buttons.Length; i++)
                buttons[i] = new RectangleF(strip.Right - (buttons.Length - i) * bw, strip.Y, bw, stripH);

            if (updateLabel != null)
            {
                float uw = Math.Min(stripH * 6.5f, strip.Width * 0.3f);
                updateButton = new RectangleF(buttons[0].Left - uw - 6 * scale, strip.Y + 1, uw, stripH - 2);
            }
            else updateButton = RectangleF.Empty;

            RectangleF content = new RectangleF(pad, strip.Bottom + 1, cs.Width - 2 * pad, cs.Height - pad - strip.Bottom - 1);
            contentW = content.Width;
            contentH = content.Height;
            float unitW = content.Width / KeyLayout.Columns(showNumpad);
            unitH = content.Height / KeyLayout.Rows(showFnRow);
            float gap = Math.Max(2f, Math.Min(unitW, unitH) * 0.075f);
            foreach (KeyDef k in Keys)
            {
                int row = k.Row;
                float kx = k.X, kw = k.W, kh = KeyLayout.RowHeight(k);
                if (!showFnRow && !k.Numpad)
                {
                    // Sin fila de funciones, Esc pasa a la izquierda de la fila de números (tamaño normal):
                    // las 13 teclas de esa fila se estrechan un 4 % y Borrar queda de 1,5 unidades.
                    if (k.Id == "Esc") { row = 1; kx = 0; kw = 1; kh = 1; }
                    else if (k.Row == 1)
                    {
                        if (k.Id == "Back") { kx = 13.5f; kw = 1.5f; }
                        else { kx = 1 + k.X * 12.5f / 13f; kw = k.W * 12.5f / 13f; }
                    }
                }
                if ((k.Numpad && !showNumpad) || (row == 0 && !showFnRow)) { k.Rect = RectangleF.Empty; continue; }
                float y = content.Y + KeyLayout.RowTop(row, showFnRow) * unitH;
                k.Rect = new RectangleF(content.X + kx * unitW + gap / 2, y + gap / 2, kw * unitW - gap, kh * unitH - gap);
            }
        }

        KeyDef KeyAt(Point p)
        {
            foreach (KeyDef k in Keys) if (k.Rect.Contains(p)) return k;
            return null;
        }

        Button ButtonAt(Point p)
        {
            for (int i = 0; i < buttons.Length; i++) if (buttons[i].Contains(p)) return (Button)i;
            return Button.None;
        }

        int EdgesAt(Point p)
        {
            Size cs = ClientSize;
            int corner = edge * 3;
            int e = 0;
            if (p.X < edge) e |= EdgeL;
            if (p.X >= cs.Width - edge) e |= EdgeR;
            if (p.Y < edge) e |= EdgeT;
            if (p.Y >= cs.Height - edge) e |= EdgeB;
            if ((e & (EdgeL | EdgeR)) != 0) { if (p.Y < corner) e |= EdgeT; else if (p.Y >= cs.Height - corner) e |= EdgeB; }
            if ((e & (EdgeT | EdgeB)) != 0) { if (p.X < corner) e |= EdgeL; else if (p.X >= cs.Width - corner) e |= EdgeR; }
            return e;
        }

        static Cursor CursorFor(int e)
        {
            if (e == (EdgeL | EdgeT) || e == (EdgeR | EdgeB)) return Cursors.SizeNWSE;
            if (e == (EdgeR | EdgeT) || e == (EdgeL | EdgeB)) return Cursors.SizeNESW;
            if ((e & (EdgeL | EdgeR)) != 0) return Cursors.SizeWE;
            if ((e & (EdgeT | EdgeB)) != 0) return Cursors.SizeNS;
            return Cursors.Default;
        }

        // ------------------------------------------------------------------ ratón

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                if (MenuRequested != null) MenuRequested(this, new ScreenPointEventArgs(PointToScreen(e.Location)));
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            int edges = EdgesAt(e.Location);
            if (edges != 0) { BeginDrag(Drag.Resize, edges); return; }

            if (!updateButton.IsEmpty && updateButton.Contains(e.Location))
            {
                updatePressed = true;
                Invalidate(Rectangle.Ceiling(updateButton));
                return;
            }

            Button b = ButtonAt(e.Location);
            if (b != Button.None) { pressedButton = b; Invalidate(Rectangle.Ceiling(buttons[(int)b])); return; }

            KeyDef k = KeyAt(e.Location);
            if (k != null)
            {
                pressed = k;
                Press(k);
                if (k.Repeat) { repeatTimer.Interval = 420; repeatTimer.Start(); }
                return;
            }
            BeginDrag(Drag.Move, 0);
        }

        void BeginDrag(Drag mode, int edges)
        {
            drag = mode;
            dragEdges = edges;
            dragStart = Cursor.Position;
            dragBounds = Bounds;
            if (mode == Drag.Move) Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag != Drag.None) { DoDrag(); return; }
            if (e.Button != MouseButtons.None) return;

            Cursor = CursorFor(EdgesAt(e.Location));
            KeyDef k = KeyAt(e.Location);
            Button b = ButtonAt(e.Location);
            if (k != hover) { InvalidateKey(hover); hover = k; InvalidateKey(hover); }
            if (b != hoverButton)
            {
                if (hoverButton != Button.None) Invalidate(Rectangle.Ceiling(buttons[(int)hoverButton]));
                hoverButton = b;
                if (b != Button.None) Invalidate(Rectangle.Ceiling(buttons[(int)b]));
            }
        }

        void DoDrag()
        {
            Point c = Cursor.Position;
            int dx = c.X - dragStart.X, dy = c.Y - dragStart.Y;
            if (drag == Drag.Move)
            {
                Location = new Point(dragBounds.X + dx, dragBounds.Y + dy);
                return;
            }
            int l = dragBounds.Left, t = dragBounds.Top, r = dragBounds.Right, b = dragBounds.Bottom;
            int minW = MinimumSize.Width, minH = MinimumSize.Height;
            if ((dragEdges & EdgeL) != 0) l = Math.Min(l + dx, r - minW);
            if ((dragEdges & EdgeR) != 0) r = Math.Max(r + dx, l + minW);
            if ((dragEdges & EdgeT) != 0) t = Math.Min(t + dy, b - minH);
            if ((dragEdges & EdgeB) != 0) b = Math.Max(b + dy, t + minH);
            Bounds = Rectangle.FromLTRB(l, t, r, b);
            Update(); // pinta ya, sin esperar a la cola de mensajes: redimensionado suave
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Button clicked = pressedButton != Button.None && ButtonAt(e.Location) == pressedButton ? pressedButton : Button.None;
            bool updateClicked = updatePressed && updateButton.Contains(e.Location);
            EndInteraction();
            if (updateClicked && UpdateRequested != null) { UpdateRequested(this, EventArgs.Empty); return; }
            switch (clicked)
            {
                case Button.Menu:
                    if (MenuRequested != null)
                    {
                        RectangleF r = buttons[(int)Button.Menu];
                        MenuRequested(this, new ScreenPointEventArgs(PointToScreen(new Point((int)r.Left, (int)r.Bottom))));
                    }
                    break;
                case Button.Pin:
                    KeyboardTopMost = !topMost;
                    if (TopMostToggled != null) TopMostToggled(this, EventArgs.Empty);
                    break;
                case Button.Minimize:
                    if (MinimizeRequested != null) MinimizeRequested(this, EventArgs.Empty);
                    break;
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (drag != Drag.None) return;
            InvalidateKey(hover);
            hover = null;
            if (hoverButton != Button.None) Invalidate(Rectangle.Ceiling(buttons[(int)hoverButton]));
            hoverButton = Button.None;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) EndInteraction();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!strip.Contains(e.Location)) return;
            double o = Math.Max(0.3, Math.Min(1.0, Opacity + (e.Delta > 0 ? 0.05 : -0.05)));
            Opacity = o;
            if (BoundsCommitted != null) BoundsCommitted(this, EventArgs.Empty);
        }

        void EndInteraction()
        {
            repeatTimer.Stop();
            bool committed = drag != Drag.None;
            drag = Drag.None;
            Cursor = Cursors.Default;
            if (pressed != null) { InvalidateKey(pressed); pressed = null; }
            if (pressedButton != Button.None) { Invalidate(Rectangle.Ceiling(buttons[(int)pressedButton])); pressedButton = Button.None; }
            if (updatePressed) { updatePressed = false; Invalidate(Rectangle.Ceiling(updateButton)); }
            if (committed && BoundsCommitted != null) BoundsCommitted(this, EventArgs.Empty);
        }

        void RepeatTick(object sender, EventArgs e)
        {
            if (pressed == null || !pressed.Repeat) { repeatTimer.Stop(); return; }
            repeatTimer.Interval = 45;
            Press(pressed);
        }

        void InvalidateKey(KeyDef k)
        {
            if (k != null) Invalidate(Rectangle.Inflate(Rectangle.Ceiling(k.Rect), 2, 2));
        }

        // ------------------------------------------------------------------ lógica de teclas

        bool IsDead(string s) { return layout.DeadKeys.Contains(s); }

        static string Compose(string dead, string c)
        {
            string mark;
            switch (dead)
            {
                case "´": mark = "́"; break;
                case "`": mark = "̀"; break;
                case "^": mark = "̂"; break;
                case "¨": mark = "̈"; break;
                default: return null;
            }
            // Igual que la distribución española de Windows: solo vocales (y la «y» con ´ y ¨)
            if (c.Length != 1) return null;
            bool y = (c == "y" || c == "Y") && (dead == "´" || dead == "¨");
            if ("aeiouAEIOU".IndexOf(c[0]) < 0 && !y) return null;
            string r = (c + mark).Normalize(System.Text.NormalizationForm.FormC);
            return r.Length == 1 ? r : null;
        }

        string CharFor(KeyDef k)
        {
            if (altGr && k.AltGr != null) return k.AltGr;
            bool upper = k.IsLetter ? shift ^ caps : shift;
            return upper ? k.Shifted : k.Normal;
        }

        bool HasSystemModifiers { get { return ctrl || alt || win; } }

        void ClearOneShot()
        {
            shift = ctrl = alt = altGr = win = false;
        }

        /// <summary>Pulsación de una tecla del teclado en pantalla.</summary>
        public void Press(KeyDef k)
        {
            switch (k.Kind)
            {
                case KeyKind.Shift: shift = !shift; break;
                case KeyKind.Caps: caps = !caps; break;
                case KeyKind.NumLock: numLock = !numLock; break;
                case KeyKind.Ctrl: ctrl = !ctrl; break;
                case KeyKind.Alt: alt = !alt; break;
                case KeyKind.AltGr: altGr = !altGr; break;
                case KeyKind.Win:
                    // Win + Win abre el menú Inicio; Win + tecla hace el atajo (Win+E, Win+D...)
                    if (win) { Input.SendVk(Input.VK_LWIN, false, false, false, false); win = false; }
                    else win = true;
                    break;
                case KeyKind.Char: TypeChar(k); break;
                case KeyKind.Vk: TypeVk(k); break;
            }
            Invalidate();
        }

        void TypeChar(KeyDef k)
        {
            if (k.Numpad && !numLock && k.NavLabel != null)
            {
                // Bloq Num desactivado: 7 = Inicio, 8 = ↑, 0 = Insert, . = Supr...
                FlushPending();
                if (k.NavVk != 0) Input.SendVk(k.NavVk, shift, ctrl, alt, win);
                ClearOneShot();
                return;
            }
            string c = CharFor(k);
            if (HasSystemModifiers)
            {
                FlushPending();
                Input.SendCombo(k.IsLetter ? k.Normal[0] : c[0], shift, ctrl, alt, win);
            }
            else if (pending != null)
            {
                string p = pending;
                pending = null;
                Input.SendText(Compose(p, c) ?? p + c);
            }
            else if (IsDead(c)) pending = c;
            else Input.SendText(c);
            ClearOneShot();
        }

        void TypeVk(KeyDef k)
        {
            if (pending != null && !HasSystemModifiers)
            {
                if (k.Vk == Input.VK_SPACE) { Input.SendText(pending); pending = null; ClearOneShot(); return; }
                if (k.Vk == Input.VK_BACK || k.Vk == Input.VK_ESCAPE) { pending = null; ClearOneShot(); return; }
            }
            FlushPending();
            if (k.Vk == Input.VK_SPACE && !HasSystemModifiers) Input.SendText(" ");
            else Input.SendVk(k.Vk, shift, ctrl, alt, win);
            ClearOneShot();
        }

        void FlushPending()
        {
            if (pending == null) return;
            Input.SendText(pending);
            pending = null;
        }

        bool IsLatched(KeyDef k)
        {
            switch (k.Kind)
            {
                case KeyKind.Shift: return shift;
                case KeyKind.Caps: return caps;
                case KeyKind.Ctrl: return ctrl;
                case KeyKind.Alt: return alt;
                case KeyKind.AltGr: return altGr;
                case KeyKind.Win: return win;
                case KeyKind.Char: return pending != null && CharFor(k) == pending;
            }
            return false;
        }

        // ------------------------------------------------------------------ pintado

        protected override void OnPaint(PaintEventArgs e)
        {
            Render(e.Graphics, e.ClipRectangle);
        }

        public void Render(Graphics g, Rectangle clip)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (SolidBrush bg = new SolidBrush(Theme.Back)) g.FillRectangle(bg, clip);
            using (Pen border = new Pen(Theme.Border)) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            if (clip.IntersectsWith(Rectangle.Ceiling(strip))) DrawStrip(g);
            foreach (KeyDef k in Keys)
                if (!k.Rect.IsEmpty && clip.IntersectsWith(Rectangle.Ceiling(k.Rect))) DrawKey(g, k);
        }

        void DrawStrip(Graphics g)
        {
            // asa de arrastre centrada
            float gw = Math.Min(48 * scale, strip.Width * 0.12f), gh = Math.Max(3f, 4 * scale);
            RectangleF grip = new RectangleF(strip.X + (strip.Width - gw) / 2, strip.Y + (strip.Height - gh) / 2, gw, gh);
            using (GraphicsPath p = Theme.RoundRect(grip, gh / 2))
            using (SolidBrush b = new SolidBrush(Theme.Grip))
                g.FillPath(b, p);

            if (!updateButton.IsEmpty)
            {
                using (GraphicsPath p = Theme.RoundRect(updateButton, updateButton.Height / 2))
                using (SolidBrush b = new SolidBrush(updatePressed ? Color.FromArgb(0, 90, 170) : Theme.Accent))
                    g.FillPath(b, p);
                using (SolidBrush tb = new SolidBrush(Theme.OnAccent))
                    DrawFitted(g, updateLabel, Theme.TextFamily, updateButton.Height * 0.55f, tb,
                               RectangleF.Inflate(updateButton, -updateButton.Height * 0.3f, 0));
            }

            string[] glyphs = { "", topMost ? "" : "", "" };
            Font f = fonts.Get(Theme.IconFamily, strip.Height * 0.48f, FontStyle.Regular);
            for (int i = 0; i < buttons.Length; i++)
            {
                RectangleF r = buttons[i];
                if ((int)pressedButton == i || (int)hoverButton == i)
                    using (GraphicsPath p = Theme.RoundRect(RectangleF.Inflate(r, -1, -1), 4 * scale))
                    using (SolidBrush b = new SolidBrush((int)pressedButton == i ? Theme.Accent : Theme.KeySpecial))
                        g.FillPath(b, p);
                Color c = i == (int)Button.Pin && topMost ? Theme.AltGrText : Theme.SubText;
                using (SolidBrush tb = new SolidBrush(c)) g.DrawString(glyphs[i], f, tb, r, center);
            }
        }

        void DrawKey(Graphics g, KeyDef k)
        {
            RectangleF r = k.Rect;
            bool latched = IsLatched(k);
            Color fill = k == pressed ? Theme.Accent
                       : latched ? Theme.Latched
                       : k == hover ? Theme.KeyHover
                       : k.IsSpecial ? Theme.KeySpecial : Theme.Key;

            using (GraphicsPath p = Theme.RoundRect(r, Math.Min(r.Width, r.Height) * 0.14f))
            using (SolidBrush b = new SolidBrush(fill))
                g.FillPath(b, p);

            float main = unitH * (k.Row == 0 && showFnRow ? 0.30f : 0.40f);
            float sub = unitH * 0.23f;
            using (SolidBrush text = new SolidBrush(k == pressed ? Theme.OnAccent : Theme.Text))
            using (SolidBrush subText = new SolidBrush(Theme.SubText))
            using (SolidBrush altText = new SolidBrush(Theme.AltGrText))
            {
                if (k.Kind == KeyKind.Win)
                {
                    float s = unitH * 0.26f, gp = Math.Max(1f, s * 0.1f), q = (s - gp) / 2;
                    float x0 = r.X + (r.Width - s) / 2, y0 = r.Y + (r.Height - s) / 2;
                    g.SmoothingMode = SmoothingMode.None;
                    g.FillRectangle(text, x0, y0, q, q);
                    g.FillRectangle(text, x0 + q + gp, y0, q, q);
                    g.FillRectangle(text, x0, y0 + q + gp, q, q);
                    g.FillRectangle(text, x0 + q + gp, y0 + q + gp, q, q);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    return;
                }
                if (k.Kind != KeyKind.Char)
                {
                    DrawFitted(g, k.Label, k.Icon ? Theme.IconFamily : Theme.TextFamily,
                               k.Icon ? main * 0.85f : main * 0.78f, text, r);
                    if (k.Kind == KeyKind.Caps || k.Kind == KeyKind.NumLock)
                    {
                        bool on = k.Kind == KeyKind.Caps ? caps : numLock;
                        float d = Math.Max(4f, unitH * 0.1f);
                        using (SolidBrush led = new SolidBrush(on ? Theme.LedOn : Theme.LedOff))
                            g.FillEllipse(led, r.Right - d * 2.2f, r.Y + d * 1.2f, d, d);
                    }
                    return;
                }

                if (k.Numpad && !numLock && k.NavLabel != null)
                {
                    DrawFitted(g, k.NavLabel, k.Icon ? Theme.IconFamily : Theme.TextFamily,
                               k.Icon ? main * 0.85f : main * 0.62f, subText, r);
                    return;
                }
                string output = CharFor(k);
                string shown = output;
                if (pending != null && !IsDead(output)) shown = Compose(pending, output) ?? output;
                DrawFitted(g, shown, Theme.TextFamily, main, text, r);

                if (!k.IsLetter && !altGr)
                {
                    string alternate = shift ? k.Normal : k.Shifted;
                    if (alternate != output)
                    {
                        Font sf = fonts.Get(Theme.TextFamily, sub, TextStyle);
                        g.DrawString(alternate, sf, subText, r.X + r.Width * 0.08f, r.Y + r.Height * 0.04f);
                    }
                }
                if (k.AltGr != null && !altGr)
                {
                    Font af = fonts.Get(Theme.TextFamily, sub, TextStyle);
                    SizeF sz = g.MeasureString(k.AltGr, af);
                    g.DrawString(k.AltGr, af, altText, r.Right - sz.Width - r.Width * 0.06f, r.Bottom - sz.Height - r.Height * 0.02f);
                }
            }
        }

        void DrawFitted(Graphics g, string s, string family, float px, Brush brush, RectangleF r)
        {
            if (string.IsNullOrEmpty(s)) return;
            FontStyle style = family == Theme.TextFamily ? TextStyle : FontStyle.Regular; // los iconos no tienen negrita
            Font f = fonts.Get(family, px, style);
            SizeF sz = g.MeasureString(s, f);
            float max = r.Width * 0.9f;
            if (sz.Width > max) f = fonts.Get(family, px * max / sz.Width, style);
            g.DrawString(s, f, brush, r, center);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fonts.Dispose();
                repeatTimer.Dispose();
                center.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
