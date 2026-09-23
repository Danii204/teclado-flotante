using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TecladoFlotante
{
    /// <summary>
    /// Menús desplegables pensados para tablet: letra grande, opciones altas (fáciles de tocar con el dedo)
    /// y colores del tema claro/oscuro del teclado.
    /// </summary>
    public static class TouchMenu
    {
        public const float FontSize = 12f;

        public static void Style(ToolStrip menu)
        {
            menu.Renderer = new ThemedMenuRenderer();
            menu.Font = new Font(Theme.TextFamily, FontSize);
            menu.ShowItemToolTips = false;
            StyleItems(menu.Items);
        }

        static void StyleItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                if (item is ToolStripSeparator) continue;
                bool bold = item.Font.Bold;
                item.Font = new Font(Theme.TextFamily, FontSize, bold ? FontStyle.Bold : FontStyle.Regular);
                item.Padding = new Padding(6, 9, 6, 9);
                ToolStripDropDownItem dd = item as ToolStripDropDownItem;
                if (dd != null && dd.HasDropDownItems)
                {
                    dd.DropDown.Renderer = new ThemedMenuRenderer();
                    dd.DropDown.Font = new Font(Theme.TextFamily, FontSize);
                    StyleItems(dd.DropDownItems);
                }
            }
        }
    }

    /// <summary>Renderizador con los colores actuales de <see cref="Theme"/> (se leen al pintar: cambia al vuelo).</summary>
    internal class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new ThemedColors()) { RoundedEdges = true; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.SubText;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Marca de verificación dibujada a mano: se ve igual de bien en claro y en oscuro
            Rectangle r = e.ImageRectangle;
            r.Inflate(2, 2);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Theme.RoundRect(r, 4))
            using (SolidBrush b = new SolidBrush(Theme.Accent))
                g.FillPath(b, p);
            float w = r.Width;
            using (Pen pen = new Pen(Color.White, Math.Max(2f, w / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(r.X + w * 0.25f, r.Y + w * 0.52f),
                    new PointF(r.X + w * 0.43f, r.Y + w * 0.70f),
                    new PointF(r.X + w * 0.76f, r.Y + w * 0.32f),
                });
        }
    }

    internal class ThemedColors : ProfessionalColorTable
    {
        static Color Menu { get { return Theme.IsDark ? Color.FromArgb(43, 43, 46) : Color.FromArgb(251, 251, 252); } }
        static Color Hover { get { return Theme.IsDark ? Color.FromArgb(62, 62, 68) : Color.FromArgb(226, 238, 252); } }

        public override Color ToolStripDropDownBackground { get { return Menu; } }
        public override Color ImageMarginGradientBegin { get { return Menu; } }
        public override Color ImageMarginGradientMiddle { get { return Menu; } }
        public override Color ImageMarginGradientEnd { get { return Menu; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Hover; } }
        public override Color MenuItemSelected { get { return Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return Hover; } }
        public override Color MenuItemPressedGradientEnd { get { return Hover; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Menu; } }
        public override Color CheckBackground { get { return Theme.Accent; } }
        public override Color CheckSelectedBackground { get { return Theme.Accent; } }
        public override Color CheckPressedBackground { get { return Theme.Accent; } }
    }

    /// <summary>
    /// Aviso para tablet que sustituye a MessageBox: siempre por encima de todo (también del teclado,
    /// que va «siempre encima»), colocado donde no lo tape el teclado y con botones grandes.
    /// MessageBox no sirve: queda debajo del teclado y además lo bloquea, y sin teclado físico no hay salida.
    /// </summary>
    public class TouchDialog : Form
    {
        /// <summary>Área que el aviso debe evitar tapar (el teclado o el botón flotante). Vacía si no hay.</summary>
        public static Func<Rectangle> AvoidArea = () => Rectangle.Empty;

        /// <summary>
        /// Ventana propietaria (el teclado, si está visible). Una ventana «propiedad» de otra queda SIEMPRE
        /// por encima de ella: aunque el teclado vuelva a ponerse delante, no puede tapar el aviso.
        /// </summary>
        public static Func<Form> OwnerWindow = () => null;

        /// <summary>Muestra el aviso y espera. Devuelve true si se pulsa el botón principal.</summary>
        public static bool Show(string title, string message, string primary, string secondary)
        {
            using (TouchDialog d = new TouchDialog(title, message, primary, secondary))
            {
                Form owner = OwnerWindow();
                if (owner != null && (!owner.Visible || owner.IsDisposed)) owner = null;
                d.PlaceAvoiding(owner != null ? owner.Bounds : AvoidArea());
                return (owner != null ? d.ShowDialog(owner) : d.ShowDialog()) == DialogResult.OK;
            }
        }

        public static void Info(string title, string message)
        {
            Show(title, message, "Aceptar", null);
        }

        public TouchDialog(string title, string message, string primary, string secondary)
        {
            Text = "Teclado Flotante";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font(Theme.TextFamily, 12f);

            float s = DeviceDpi / 96f;
            Func<float, int> px = v => (int)Math.Round(v * s);
            int width = px(480), margin = px(24), textW = width - 2 * margin;

            Font titleFont = new Font(Theme.TextFamily, 17f, FontStyle.Bold);
            int titleH = TextRenderer.MeasureText(title, titleFont, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak).Height;
            int bodyH = string.IsNullOrEmpty(message) ? 0
                : TextRenderer.MeasureText(message, Font, new Size(textW, int.MaxValue), TextFormatFlags.WordBreak).Height;

            Label titleLabel = new Label { Text = title, Font = titleFont, AutoSize = false, Bounds = new Rectangle(margin, px(20), textW, titleH) };
            Label body = new Label
            {
                Text = message, AutoSize = false, ForeColor = Theme.SubText,
                Bounds = new Rectangle(margin, titleLabel.Bottom + px(8), textW, bodyH),
            };
            int buttonsTop = body.Bottom + px(22), buttonH = px(58);

            Button ok;
            if (secondary == null)
            {
                ok = MakeButton(primary, true, new Rectangle(width - margin - px(200), buttonsTop, px(200), buttonH));
                CancelButton = ok;
            }
            else
            {
                ok = MakeButton(primary, true, new Rectangle(margin, buttonsTop, px(250), buttonH));
                Button cancel = MakeButton(secondary, false, new Rectangle(ok.Right + px(12), buttonsTop, width - margin - ok.Right - px(12), buttonH));
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);
                CancelButton = cancel;
            }
            ok.DialogResult = DialogResult.OK;
            AcceptButton = ok;
            Controls.AddRange(new Control[] { titleLabel, body, ok });
            ClientSize = new Size(width, buttonsTop + buttonH + margin);
        }

        /// <summary>Centrado en la pantalla; si así tapa (o queda tapado por) el teclado, se pone encima o debajo de él.</summary>
        public void PlaceAvoiding(Rectangle avoid)
        {
            Screen screen = avoid.IsEmpty ? Screen.PrimaryScreen : Screen.FromRectangle(avoid);
            Rectangle wa = screen.WorkingArea;
            Rectangle r = new Rectangle(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2, Width, Height);
            if (!avoid.IsEmpty && r.IntersectsWith(avoid))
            {
                int above = avoid.Top - wa.Top, below = wa.Bottom - avoid.Bottom;
                if (above >= Height + 16) r.Y = avoid.Top - Height - 16;
                else if (below >= Height + 16) r.Y = avoid.Bottom + 16;
                else r.Y = above >= below ? wa.Top + 8 : wa.Bottom - Height - 8; // no cabe: el más despejado (y queda encima)
            }
            StartPosition = FormStartPosition.Manual;
            Location = r.Location;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Por si otra ventana «siempre encima» se adelantó: al frente de todo
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE);
            Activate();
        }

        static Button MakeButton(string text, bool primary, Rectangle bounds)
        {
            Button b = new Button
            {
                Text = text,
                Bounds = bounds,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Theme.Accent : Theme.KeySpecial,
                ForeColor = primary ? Color.White : Theme.Text,
                Font = new Font(Theme.TextFamily, 13f, primary ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Theme.Border;
            return b;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle, Theme.IsDark);
        }
    }
}
