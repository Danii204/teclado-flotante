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

    /// <summary>Ventana de actualización para tablet: mensaje claro y dos botones grandes.</summary>
    public class UpdateDialog : Form
    {
        public static bool Ask(string newVersion, string currentVersion)
        {
            using (UpdateDialog d = new UpdateDialog(newVersion, currentVersion))
                return d.ShowDialog() == DialogResult.OK;
        }

        public UpdateDialog(string newVersion, string currentVersion)
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
            ClientSize = new Size(px(460), px(250));

            Label title = new Label
            {
                Text = "Hay una versión nueva",
                Font = new Font(Theme.TextFamily, 17f, FontStyle.Bold),
                AutoSize = false,
                Bounds = new Rectangle(px(24), px(20), px(412), px(40)),
            };
            Label body = new Label
            {
                Text = "Versión " + newVersion + " (ahora tienes la " + currentVersion + ").\n" +
                       "El teclado se cerrará un momento y se abrirá solo.",
                AutoSize = false,
                ForeColor = Theme.SubText,
                Bounds = new Rectangle(px(24), px(66), px(412), px(80)),
            };
            Button update = MakeButton("Actualizar ahora", true, new Rectangle(px(24), px(166), px(250), px(58)));
            update.DialogResult = DialogResult.OK;
            Button later = MakeButton("Más tarde", false, new Rectangle(px(286), px(166), px(150), px(58)));
            later.DialogResult = DialogResult.Cancel;

            Controls.AddRange(new Control[] { title, body, update, later });
            AcceptButton = update;
            CancelButton = later;
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
