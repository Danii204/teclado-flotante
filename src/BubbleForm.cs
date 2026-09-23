using System;
using System.Drawing;
using System.Windows.Forms;

namespace TecladoFlotante
{
    /// <summary>Botón flotante pequeño que aparece al minimizar: un toque y vuelve el teclado.</summary>
    public class BubbleForm : Form
    {
        public event EventHandler Clicked;
        public event EventHandler Moved;
        public event EventHandler<ScreenPointEventArgs> MenuRequested;

        Point downScreen, downLocation;
        bool down, dragging;

        public BubbleForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MaximizeBox = MinimizeBox = ControlBox = false;
            Text = "Teclado Flotante";
            BackColor = Theme.Back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(48, 48);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int s = (int)Math.Round(46 * Native.DpiScale(Handle));
            Size = new Size(s, s);
            Native.RoundCorners(Handle, 2, 0x404040);
        }

        public void ShowAt(Point p)
        {
            IntPtr h = Handle;
            Rectangle r = new Rectangle(p, Size);
            if (!Settings.IsVisibleOnScreen(r, Size.Width / 2))
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                r.Location = new Point(wa.Right - Size.Width - 16, wa.Bottom - Size.Height - 16);
            }
            Location = r.Location;
            if (!Visible) Show();
            Native.SetWindowPos(h, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)Native.MA_NOACTIVATE; return; }
            base.WndProc(ref m);
        }

        bool badge;

        /// <summary>Punto naranja: hay una versión nueva disponible.</summary>
        public bool Badge
        {
            get { return badge; }
            set { badge = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Accent);
            IconArt.Draw(e.Graphics, new RectangleF(0, 0, Width, Height), Theme.Accent, Color.White);
            if (badge)
            {
                float d = Width * 0.32f;
                RectangleF r = new RectangleF(Width - d - Width * 0.06f, Width * 0.06f, d, d);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 140, 0))) e.Graphics.FillEllipse(b, r);
                using (Pen p = new Pen(Color.White, Math.Max(1.5f, d * 0.12f))) e.Graphics.DrawEllipse(p, r);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                if (MenuRequested != null) MenuRequested(this, new ScreenPointEventArgs(Cursor.Position));
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            down = true;
            dragging = false;
            downScreen = Cursor.Position;
            downLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!down) return;
            Point c = Cursor.Position;
            int dx = c.X - downScreen.X, dy = c.Y - downScreen.Y;
            int threshold = SystemInformation.DragSize.Width;
            if (!dragging && (Math.Abs(dx) > threshold || Math.Abs(dy) > threshold)) dragging = true;
            if (dragging) Location = new Point(downLocation.X + dx, downLocation.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!down || e.Button != MouseButtons.Left) return;
            down = false;
            if (dragging) { if (Moved != null) Moved(this, EventArgs.Empty); }
            else if (Clicked != null) Clicked(this, EventArgs.Empty);
            dragging = false;
        }
    }
}
