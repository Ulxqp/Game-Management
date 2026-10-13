using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GameManagement
{
    // Native components share these tokens; a web runtime is not needed.
    internal static class ModernTheme
    {
        internal static readonly Color Background = ColorTranslator.FromHtml("#F8FAFC"), Surface = Color.White, SurfaceMuted = ColorTranslator.FromHtml("#F1F5F9");
        internal static readonly Color Text = ColorTranslator.FromHtml("#0F172A"), Secondary = ColorTranslator.FromHtml("#64748B"), TextFaint = ColorTranslator.FromHtml("#94A3B8");
        internal static readonly Color Border = ColorTranslator.FromHtml("#E2E8F0"), Divider = ColorTranslator.FromHtml("#EDF2F7"), Focus = ColorTranslator.FromHtml("#93C5FD");
        internal static readonly Color Blue = ColorTranslator.FromHtml("#2563EB"), BlueHover = ColorTranslator.FromHtml("#1D4ED8"), BlueSoft = ColorTranslator.FromHtml("#DBEAFE");
        internal static readonly Color Work = Blue, Game = ColorTranslator.FromHtml("#EF4444"), GameHover = ColorTranslator.FromHtml("#DC2626"), GameSoft = ColorTranslator.FromHtml("#FEE2E2");
        internal static readonly Color Success = ColorTranslator.FromHtml("#16A34A"), SuccessSoft = ColorTranslator.FromHtml("#DCFCE7");
        internal static readonly Color Warning = ColorTranslator.FromHtml("#D97706"), WarningSoft = ColorTranslator.FromHtml("#FEF3C7"), Error = ColorTranslator.FromHtml("#DC2626");
        internal const int Space4 = 4, Space8 = 8, Space12 = 12, Space16 = 16, Space20 = 20, Space24 = 24, Space32 = 32;
        internal const int CompactHeight = 32, ControlHeight = 36, PrimaryHeight = 40, MajorActionHeight = 42, InputHeight = 36, IconSize = 18;
        internal const int Radius = 12, SmallRadius = 10, CardRadius = 14, LargeRadius = 16, PillRadius = 999;
        internal const int TitleBarHeight = 48, DockHeight = 44, RailWidth = 44, OverlayWidth = 280;
        internal const int Space = Space8, PanelPadding = Space16, LargePanelPadding = Space24, TransitionMilliseconds = 170;
        private static readonly float desktopScale = DesktopScale();
        private static float DesktopScale()
        {
            try { using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1F, graphics.DpiX / 96F); }
            catch { return 1F; }
        }
        internal static float ScaleFactor(Control control)
        {
            Form form = control == null ? null : control.FindForm();
            return Math.Max(desktopScale, form == null ? 1F : Math.Max(1F, form.Font.SizeInPoints / 9F));
        }
        internal static int Px(Control control, int value) { return (int)Math.Round(value * ScaleFactor(control), MidpointRounding.AwayFromZero); }
        internal static bool ReducedMotion { get { return !SystemInformation.IsMenuAnimationEnabled; } }
        internal static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }
        internal static Color Tint(Color accent, double amount) { return Blend(Surface, accent, amount); }
        internal static Color Blend(Color start, Color end, double progress)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            return Color.FromArgb((int)(start.R + (end.R - start.R) * progress), (int)(start.G + (end.G - start.G) * progress), (int)(start.B + (end.B - start.B) * progress));
        }
        internal static Label Label(string text, float size = 9, bool bold = false)
        {
            return new Label { Text = text, ForeColor = Text, BackColor = Color.Transparent, Font = Font(size, bold ? FontStyle.Bold : FontStyle.Regular), AutoSize = true };
        }
        internal static Button Button(string text, Action action, bool primary = false)
        {
            ModernButton button = new ModernButton { Text = text, Primary = primary, Size = new Size(144, primary ? PrimaryHeight : ControlHeight), AccessibleName = text };
            if (action != null) button.Click += delegate { action(); }; return button;
        }
        internal static Control Icon(string glyph, Color color, int size = IconSize) { return new ModernIcon { Glyph = glyph, Accent = color, Size = new Size(size, size), TabStop = false }; }
        internal static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath(); int d = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            if (d < 2) { path.AddRectangle(rectangle); return path; }
            path.AddArc(rectangle.X, rectangle.Y, d, d, 180, 90); path.AddArc(rectangle.Right - d, rectangle.Y, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90); path.AddArc(rectangle.X, rectangle.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        internal static void DrawIcon(Graphics graphics, string glyph, Rectangle bounds, Color color)
        {
            if (string.IsNullOrEmpty(glyph) || bounds.Width < 3 || bounds.Height < 3) return;
            if (glyph == "clock" || glyph == "\uE917")
            {
                Rectangle dial = Rectangle.Inflate(bounds, -2, -2);
                using (Pen pen = new Pen(color, Math.Max(1.3F, bounds.Width / 13F)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    graphics.DrawEllipse(pen, dial);
                    float x = bounds.Left + bounds.Width / 2F, y = bounds.Top + bounds.Height / 2F;
                    graphics.DrawLine(pen, x, y, x, y - bounds.Height * .23F);
                    graphics.DrawLine(pen, x, y, x + bounds.Width * .2F, y + bounds.Height * .1F);
                }
                return;
            }
            if (glyph == "folder" || glyph == "\uE8B7")
            {
                float scale = Math.Min(bounds.Width, bounds.Height) / 20F, x = bounds.Left + (bounds.Width - 20 * scale) / 2F, y = bounds.Top + (bounds.Height - 20 * scale) / 2F;
                using (GraphicsPath path = new GraphicsPath())
                using (Pen pen = new Pen(color, Math.Max(1.3F, 1.5F * scale)))
                {
                    pen.LineJoin = LineJoin.Round;
                    path.AddLines(new[] { new PointF(x + 2 * scale, y + 5 * scale), new PointF(x + 7 * scale, y + 5 * scale), new PointF(x + 9 * scale, y + 7 * scale), new PointF(x + 18 * scale, y + 7 * scale), new PointF(x + 18 * scale, y + 16 * scale), new PointF(x + 2 * scale, y + 16 * scale) });
                    path.CloseFigure(); graphics.DrawPath(pen, path); graphics.DrawLine(pen, x + 2 * scale, y + 9 * scale, x + 18 * scale, y + 9 * scale);
                }
                return;
            }
            if (glyph == "info" || glyph == "error" || glyph == "warning" || glyph == "check")
            {
                Rectangle dial = Rectangle.Inflate(bounds, -2, -2);
                using (Pen pen = new Pen(color, Math.Max(1.3F, bounds.Width / 13F)))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                    if (glyph == "warning") graphics.DrawPolygon(pen, new[] { new Point(bounds.Left + bounds.Width / 2, dial.Top), new Point(dial.Right, dial.Bottom), new Point(dial.Left, dial.Bottom) });
                    else graphics.DrawEllipse(pen, dial);
                    float x = bounds.Left + bounds.Width / 2F, y = bounds.Top + bounds.Height / 2F;
                    if (glyph == "check") graphics.DrawLines(pen, new[] { new PointF(x - bounds.Width * .23F, y), new PointF(x - bounds.Width * .06F, y + bounds.Height * .17F), new PointF(x + bounds.Width * .26F, y - bounds.Height * .18F) });
                    else if (glyph == "error") { graphics.DrawLine(pen, x - bounds.Width * .17F, y - bounds.Height * .17F, x + bounds.Width * .17F, y + bounds.Height * .17F); graphics.DrawLine(pen, x + bounds.Width * .17F, y - bounds.Height * .17F, x - bounds.Width * .17F, y + bounds.Height * .17F); }
                    else if (glyph == "info") { graphics.DrawLine(pen, x, y, x, y + bounds.Height * .22F); graphics.DrawLine(pen, x, y - bounds.Height * .22F, x, y - bounds.Height * .21F); }
                    else { graphics.DrawLine(pen, x, y - bounds.Height * .12F, x, y + bounds.Height * .1F); graphics.DrawLine(pen, x, y + bounds.Height * .25F, x, y + bounds.Height * .26F); }
                }
                return;
            }
            if (glyph == "thermometer" || glyph == "temperature")
            {
                float scale = Math.Min(bounds.Width, bounds.Height) / 20F, x = bounds.Left + bounds.Width / 2F, y = bounds.Top + (bounds.Height - 20 * scale) / 2F;
                using (Pen pen = new Pen(color, 1.7F * scale))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    graphics.DrawArc(pen, x - 3 * scale, y + scale, 6 * scale, 6 * scale, 180, 180);
                    graphics.DrawLine(pen, x - 3 * scale, y + 4 * scale, x - 3 * scale, y + 12 * scale); graphics.DrawLine(pen, x + 3 * scale, y + 4 * scale, x + 3 * scale, y + 12 * scale);
                    graphics.DrawArc(pen, x - 5 * scale, y + 10 * scale, 10 * scale, 9 * scale, -42, 264); graphics.DrawLine(pen, x, y + 7 * scale, x, y + 14 * scale);
                }
                using (Brush brush = new SolidBrush(color)) graphics.FillEllipse(brush, x - 2 * scale, y + 13 * scale, 4 * scale, 4 * scale); return;
            }
            if (glyph == "game") glyph = "\uE7FC"; if (glyph == "work") glyph = "\uE821"; if (glyph == "clock") glyph = "\uE917"; if (glyph == "cycles") glyph = "\uE895";
            if (glyph == "check") glyph = "\uE73E"; if (glyph == "warning") glyph = "\uE7BA"; if (glyph == "error") glyph = "\uEA39"; if (glyph == "info") glyph = "\uE946";
            using (Font iconFont = new Font("Segoe MDL2 Assets", Math.Max(6, Math.Min(bounds.Width, bounds.Height) * .72F), FontStyle.Regular, GraphicsUnit.Pixel))
                TextRenderer.DrawText(graphics, glyph, iconFont, bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        internal static void Grid(DataGridView grid)
        {
            grid.BackgroundColor = Surface; grid.BorderStyle = BorderStyle.None; grid.Font = Font(9); grid.ForeColor = Text; grid.GridColor = Divider;
            grid.RowHeadersVisible = false; grid.EnableHeadersVisualStyles = false; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = SurfaceMuted, ForeColor = Text, SelectionBackColor = SurfaceMuted, SelectionForeColor = Text, Font = Font(9, FontStyle.Bold), Padding = new Padding(Space12, Space4, Space12, Space4) };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = Text, SelectionBackColor = Tint(Blue, .08), SelectionForeColor = Text, Padding = new Padding(Space12, Space4, Space12, Space4), WrapMode = DataGridViewTriState.False };
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.ColumnHeadersHeight = ControlHeight; grid.RowTemplate.Height = 56;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false; grid.ReadOnly = true; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None; grid.ScrollBars = ScrollBars.Both;
            grid.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) grid.InvalidateRow(e.RowIndex); };
            grid.CellMouseLeave += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count) grid.InvalidateRow(e.RowIndex); };
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || grid[e.ColumnIndex, e.RowIndex] is ModernActionCell) return;
                Point position = grid.PointToClient(Control.MousePosition); DataGridView.HitTestInfo hit = grid.HitTest(position.X, position.Y);
                bool hovered = hit.RowIndex == e.RowIndex, selected = (e.State & DataGridViewElementStates.Selected) != 0;
                using (Brush fill = new SolidBrush(selected ? Tint(Blue, .08) : hovered ? Tint(Blue, .025) : Surface)) e.Graphics.FillRectangle(fill, e.CellBounds);
                e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground | DataGridViewPaintParts.ErrorIcon);
                using (Pen border = new Pen(Border)) e.Graphics.DrawLine(border, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1); e.Handled = true;
            };
        }
    }
    internal class ModernButton : Button
    {
        private bool primary;
        internal bool Primary { get { return primary; } set { primary = value; Invalidate(); } }
        internal Color Accent = ModernTheme.Blue;
        internal string Glyph { get; set; }
        private readonly Timer hoverTimer = new Timer();
        private double hoverAmount, hoverFrom, hoverTarget;
        private DateTime hoverStarted;
        private bool pressed;
        internal ModernButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; Font = ModernTheme.Font(9, FontStyle.Bold); BackColor = ModernTheme.Surface; ForeColor = ModernTheme.Text;
            Cursor = Cursors.Hand; TabStop = true; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            hoverTimer.Interval = 15; hoverTimer.Tick += delegate
            {
                double elapsed = (DateTime.UtcNow - hoverStarted).TotalMilliseconds / ModernTheme.TransitionMilliseconds;
                hoverAmount = hoverFrom + (hoverTarget - hoverFrom) * (1 - Math.Pow(1 - Math.Min(1, elapsed), 3));
                if (elapsed >= 1) { hoverAmount = hoverTarget; hoverTimer.Stop(); } Invalidate();
            };
        }
        private void AnimateHover(double target)
        {
            hoverTimer.Stop(); hoverTarget = target;
            if (ModernTheme.ReducedMotion || !Visible) { hoverAmount = target; Invalidate(); return; }
            hoverFrom = hoverAmount; hoverStarted = DateTime.UtcNow; hoverTimer.Start();
        }
        protected override void OnMouseEnter(EventArgs e) { AnimateHover(1); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { AnimateHover(0); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { hoverTimer.Stop(); hoverAmount = 0; pressed = false; } Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) { hoverTimer.Stop(); hoverAmount = 0; } base.OnVisibleChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? ModernTheme.Background : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3); if (r.Width < 8 || r.Height < 8) return;
            Color baseColor = Primary ? Accent : BackColor, hoverColor = Primary ? ModernTheme.Blend(Accent, Color.Black, .09) : ModernTheme.Tint(Accent, .065);
            Color fillColor = !Enabled ? ModernTheme.Border : pressed ? ModernTheme.Blend(baseColor, Color.Black, .12) : ModernTheme.Blend(baseColor, hoverColor, hoverAmount);
            using (GraphicsPath shape = ModernTheme.Rounded(r, ModernTheme.Px(this, ModernTheme.Radius)))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen border = new Pen(Focused && Enabled ? ModernTheme.Focus : Primary && Enabled ? Accent : ModernTheme.Border, Focused && Enabled ? 2 : 1)) { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape); }
            Color color = !Enabled ? ModernTheme.Secondary : Primary ? Color.White : ForeColor;
            int compactHeight = ModernTheme.Px(this, ModernTheme.CompactHeight), iconSize = ModernTheme.Px(this, ModernTheme.IconSize), gap = ModernTheme.Px(this, ModernTheme.Space8);
            Rectangle textBounds = Rectangle.Inflate(r, -ModernTheme.Px(this, Height <= compactHeight ? ModernTheme.Space4 : ModernTheme.Space8), 0);
            if (!string.IsNullOrEmpty(Glyph))
            {
                int textWidth = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, r.Width - iconSize - gap * 3), r.Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                int contentWidth = Math.Min(r.Width - gap * 2, textWidth + iconSize + (string.IsNullOrEmpty(Text) ? 0 : gap)), contentX = r.Left + (r.Width - contentWidth) / 2;
                ModernTheme.DrawIcon(e.Graphics, Glyph, new Rectangle(contentX, r.Top + (r.Height - iconSize) / 2, iconSize, iconSize), color);
                textBounds = new Rectangle(contentX + iconSize + gap, r.Top, Math.Max(0, contentWidth - iconSize - gap), r.Height);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing) { if (disposing) hoverTimer.Dispose(); base.Dispose(disposing); }
    }
    internal class ModernCard : Panel
    {
        internal int CornerRadius { get; set; }
        internal ModernCard() { BackColor = ModernTheme.Surface; Padding = new Padding(ModernTheme.PanelPadding); SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? ModernTheme.Background : Parent.BackColor); if (Width < 3 || Height < 3) return; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = ModernTheme.Px(this, CornerRadius <= 0 ? ModernTheme.CardRadius : CornerRadius);
            using (GraphicsPath shadow = ModernTheme.Rounded(new Rectangle(1, 2, Width - 3, Height - 3), radius))
            using (Brush shade = new SolidBrush(Color.FromArgb(10, ModernTheme.Text))) e.Graphics.FillPath(shade, shadow);
            using (GraphicsPath shape = ModernTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 2), radius)) using (Brush fill = new SolidBrush(BackColor)) e.Graphics.FillPath(fill, shape);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 3 || Height < 3) return; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int radius = ModernTheme.Px(this, CornerRadius <= 0 ? ModernTheme.CardRadius : CornerRadius);
            using (GraphicsPath shape = ModernTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 2), radius)) using (Pen border = new Pen(ModernTheme.Border)) e.Graphics.DrawPath(border, shape);
        }
    }
    internal sealed class ModernIcon : Control
    {
        internal string Glyph { get; set; }
        internal Color Accent { get; set; }
        internal bool Circle { get; set; }
        internal ModernIcon() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Rectangle bounds = ClientRectangle;
            if (Circle)
            {
                using (Brush fill = new SolidBrush(ModernTheme.Tint(Accent, .09))) e.Graphics.FillEllipse(fill, 0, 0, Width - 1, Height - 1);
                int size = Math.Min(ModernTheme.Px(this, ModernTheme.Space24), Math.Min(Width, Height) - ModernTheme.Px(this, ModernTheme.Space8)); bounds = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
            }
            ModernTheme.DrawIcon(e.Graphics, Glyph, bounds, Accent);
        }
    }
    internal sealed class StatusBadge : Label
    {
        private Color accent = ModernTheme.Success;
        internal Color Accent { get { return accent; } set { accent = value; Invalidate(); } }
        internal StatusBadge()
        {
            AutoSize = false; BackColor = Color.Transparent; Font = ModernTheme.Font(8.5F, FontStyle.Bold); Size = new Size(120, ModernTheme.CompactHeight); AccessibleRole = AccessibleRole.StaticText;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Rectangle bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using (GraphicsPath path = ModernTheme.Rounded(bounds, Height / 2)) using (Brush fill = new SolidBrush(ModernTheme.Tint(Accent, .1))) e.Graphics.FillPath(fill, path);
            int textWidth = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            bool showDot = textWidth + ModernTheme.Px(this, 33) <= Width;
            int dotSize = ModernTheme.Px(this, 8);
            if (showDot) using (Brush dot = new SolidBrush(Accent)) e.Graphics.FillEllipse(dot, ModernTheme.Px(this, 12), (Height - dotSize) / 2, dotSize, dotSize);
            Rectangle textBounds = showDot ? new Rectangle(ModernTheme.Px(this, 25), 0, Math.Max(0, Width - ModernTheme.Px(this, 33)), Height) : new Rectangle(ModernTheme.Px(this, 4), 0, Math.Max(0, Width - ModernTheme.Px(this, 8)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ModernTheme.Blend(Accent, ModernTheme.Text, .25), TextFormatFlags.VerticalCenter | (showDot ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }
    internal sealed class IconStatCard : ModernCard
    {
        internal readonly Label TitleLabel, ValueLabel;
        private readonly ModernIcon icon;
        private readonly StatusBadge status;
        internal string StatusText { get { return status.Text; } set { status.Text = value; status.Visible = !string.IsNullOrEmpty(value); PerformLayout(); } }
        internal Color StatusAccent { get { return status.Accent; } set { status.Accent = value; } }
        internal IconStatCard(string title, string glyph, Color accent)
        {
            Height = 84; Padding = new Padding(ModernTheme.PanelPadding); icon = new ModernIcon { Glyph = glyph, Accent = accent, Circle = true, Size = new Size(44, 44) };
            TitleLabel = ModernTheme.Label(title, 8.5F); TitleLabel.ForeColor = ModernTheme.Secondary; TitleLabel.AutoSize = false;
            ValueLabel = ModernTheme.Label("Unavailable", 17, true); ValueLabel.AutoSize = false;
            status = new StatusBadge { Visible = false, Size = new Size(88, ModernTheme.CompactHeight) };
            Controls.Add(icon); Controls.Add(TitleLabel); Controls.Add(ValueLabel); Controls.Add(status);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (icon == null) return;
            int padding = ModernTheme.Px(this, ModernTheme.PanelPadding), gap = ModernTheme.Px(this, ModernTheme.Space12), iconSize = ModernTheme.Px(this, 44);
            icon.SetBounds(padding, Math.Max(ModernTheme.Px(this, ModernTheme.Space8), (Height - iconSize) / 2), iconSize, iconSize);
            int textX = icon.Right + gap;
            status.Size = new Size(ModernTheme.Px(this, 88), ModernTheme.Px(this, ModernTheme.CompactHeight));
            int right = status.Visible ? Width - status.Width - ModernTheme.Px(this, ModernTheme.Space24) : Width - padding;
            int titleHeight = Math.Max(ModernTheme.Px(this, 20), TitleLabel.Font.Height + ModernTheme.Px(this, 2));
            int valueHeight = Math.Max(ModernTheme.Px(this, 31), ValueLabel.Font.Height + ModernTheme.Px(this, 4));
            int titleTop = Math.Max(ModernTheme.Px(this, ModernTheme.Space8), (Height - titleHeight - valueHeight - ModernTheme.Px(this, 4)) / 2);
            TitleLabel.SetBounds(textX, titleTop, Math.Max(ModernTheme.Px(this, 20), right - textX), titleHeight);
            ValueLabel.SetBounds(textX, titleTop + titleHeight + ModernTheme.Px(this, 4), Math.Max(ModernTheme.Px(this, 20), right - textX), valueHeight);
            status.Location = new Point(Math.Max(textX, Width - status.Width - padding), (Height - status.Height) / 2);
        }
    }
    internal sealed class ModernTimerSettingsSummary : Control
    {
        private string durationLabel = "Duration", duration = "00:00", breakDuration = "00:00";
        private bool timerEnabled, automaticSwitching, soundEnabled, notificationsEnabled;
        internal ModernTimerSettingsSummary()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent; AccessibleRole = AccessibleRole.Grouping; AccessibleName = "Current timer settings";
        }
        internal void SetValues(string modeDurationLabel, string durationText, string breakText, bool enabled, bool automatic, bool sound, bool notifications)
        {
            durationLabel = modeDurationLabel; duration = durationText; breakDuration = breakText;
            timerEnabled = enabled; automaticSwitching = automatic; soundEnabled = sound; notificationsEnabled = notifications;
            AccessibleDescription = durationLabel + " " + duration + "; Break duration " + breakDuration + "; Timer " + State(enabled) +
                "; Automatic switching " + State(automatic) + "; Sound " + State(sound) + "; Notifications " + State(notifications) + ".";
            Invalidate();
        }
        private static string State(bool value) { return value ? "On" : "Off"; }
        private void DrawValueRow(Graphics graphics, int x, int y, int width, string label, string value, Font labelFont, Font valueFont)
        {
            int rowHeight = ModernTheme.Px(this, 28), valueWidth = Math.Min(ModernTheme.Px(this, 88), Math.Max(ModernTheme.Px(this, 56), width / 3));
            TextRenderer.DrawText(graphics, label, labelFont, new Rectangle(x, y, Math.Max(1, width - valueWidth - ModernTheme.Px(this, ModernTheme.Space8)), rowHeight), ModernTheme.Secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(graphics, value, valueFont, new Rectangle(x + width - valueWidth, y, valueWidth, rowHeight), ModernTheme.Text,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        private void DrawStateRow(Graphics graphics, int x, int y, int width, string label, bool enabled, Font font)
        {
            int rowHeight = ModernTheme.Px(this, 26), dotSize = ModernTheme.Px(this, 8), valueWidth = ModernTheme.Px(this, 38);
            using (Brush dot = new SolidBrush(enabled ? ModernTheme.Success : ModernTheme.TextFaint))
                graphics.FillEllipse(dot, x, y + (rowHeight - dotSize) / 2, dotSize, dotSize);
            TextRenderer.DrawText(graphics, label, font, new Rectangle(x + ModernTheme.Px(this, ModernTheme.Space16), y, Math.Max(1, width - valueWidth - ModernTheme.Px(this, ModernTheme.Space16)), rowHeight), ModernTheme.Secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(graphics, State(enabled), font, new Rectangle(x + width - valueWidth, y, valueWidth, rowHeight), enabled ? ModernTheme.Success : ModernTheme.TextFaint,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < ModernTheme.Px(this, 220) || Height < ModernTheme.Px(this, 80)) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int gap = ModernTheme.Px(this, ModernTheme.Space24), columnWidth = (Width - gap) / 2, rightX = columnWidth + gap;
            using (Font heading = ModernTheme.Font(9, FontStyle.Bold))
            using (Font label = ModernTheme.Font(8.5F))
            using (Font value = ModernTheme.Font(9, FontStyle.Bold))
            using (Pen divider = new Pen(ModernTheme.Divider))
            {
                TextRenderer.DrawText(e.Graphics, "Durations", heading, new Rectangle(0, 0, columnWidth, ModernTheme.Px(this, 24)), ModernTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                DrawValueRow(e.Graphics, 0, ModernTheme.Px(this, 30), columnWidth, durationLabel, duration, label, value);
                DrawValueRow(e.Graphics, 0, ModernTheme.Px(this, 62), columnWidth, "Break duration", breakDuration, label, value);
                e.Graphics.DrawLine(divider, columnWidth + gap / 2, 0, columnWidth + gap / 2, Math.Min(Height, ModernTheme.Px(this, 150)));
                TextRenderer.DrawText(e.Graphics, "Behavior", heading, new Rectangle(rightX, 0, columnWidth, ModernTheme.Px(this, 24)), ModernTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                DrawStateRow(e.Graphics, rightX, ModernTheme.Px(this, 27), columnWidth, "Timer", timerEnabled, label);
                DrawStateRow(e.Graphics, rightX, ModernTheme.Px(this, 53), columnWidth, "Automatic switching", automaticSwitching, label);
                DrawStateRow(e.Graphics, rightX, ModernTheme.Px(this, 79), columnWidth, "Session sound", soundEnabled, label);
                DrawStateRow(e.Graphics, rightX, ModernTheme.Px(this, 105), columnWidth, "Notifications", notificationsEnabled, label);
            }
        }
    }
    internal sealed class ModernActionColumn : DataGridViewButtonColumn
    {
        internal ModernActionColumn(string name, string glyph, string tooltip = null)
        {
            Name = name; HeaderText = name == "Launch" ? "Actions" : ""; ToolTipText = tooltip ?? name; CellTemplate = new ModernActionCell { Glyph = glyph, ActionName = tooltip ?? name };
            Text = tooltip ?? name; UseColumnTextForButtonValue = true; Width = ModernTheme.CompactHeight + ModernTheme.Space8; MinimumWidth = ModernTheme.CompactHeight + ModernTheme.Space8;
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None; SortMode = DataGridViewColumnSortMode.NotSortable; DefaultCellStyle.Padding = Padding.Empty;
        }
    }
    internal sealed class ModernActionCell : DataGridViewButtonCell
    {
        internal string Glyph, ActionName;
        public override object Clone() { ModernActionCell clone = (ModernActionCell)base.Clone(); clone.Glyph = Glyph; clone.ActionName = ActionName; return clone; }
        protected override void Paint(Graphics graphics, Rectangle clipBounds, Rectangle cellBounds, int rowIndex, DataGridViewElementStates cellState, object value, object formattedValue, string errorText, DataGridViewCellStyle cellStyle, DataGridViewAdvancedBorderStyle advancedBorderStyle, DataGridViewPaintParts paintParts)
        {
            bool selected = (cellState & DataGridViewElementStates.Selected) != 0;
            Point position = DataGridView == null ? Point.Empty : DataGridView.PointToClient(Control.MousePosition);
            DataGridView.HitTestInfo hit = DataGridView == null ? null : DataGridView.HitTest(position.X, position.Y);
            bool hovered = hit != null && hit.RowIndex == rowIndex, hotAction = hovered && hit.ColumnIndex == ColumnIndex, focused = DataGridView != null && DataGridView.Focused && DataGridView.CurrentCell == this;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush fill = new SolidBrush(selected ? ModernTheme.Tint(ModernTheme.Blue, .08) : hovered ? ModernTheme.Tint(ModernTheme.Blue, .025) : ModernTheme.Surface)) graphics.FillRectangle(fill, cellBounds);
            int size = Math.Min(ModernTheme.Px(DataGridView, ModernTheme.CompactHeight), Math.Min(cellBounds.Width, cellBounds.Height) - ModernTheme.Px(DataGridView, ModernTheme.Space8)); Rectangle bounds = new Rectangle(cellBounds.Left + (cellBounds.Width - size) / 2, cellBounds.Top + (cellBounds.Height - size) / 2, size, size);
            Color accent = OwningColumn != null && OwningColumn.Name == "Remove" ? ModernTheme.Error : ModernTheme.Text;
            using (GraphicsPath shape = ModernTheme.Rounded(bounds, ModernTheme.Px(DataGridView, ModernTheme.SmallRadius))) using (Brush fill = new SolidBrush(hotAction ? ModernTheme.Tint(accent, .08) : ModernTheme.Surface)) using (Pen border = new Pen(focused ? ModernTheme.Focus : ModernTheme.Border, focused ? 2 : 1)) { graphics.FillPath(fill, shape); graphics.DrawPath(border, shape); }
            int iconSize = Math.Min(size - 4, ModernTheme.Px(DataGridView, ModernTheme.IconSize));
            ModernTheme.DrawIcon(graphics, Glyph, new Rectangle(bounds.Left + (size - iconSize) / 2, bounds.Top + (size - iconSize) / 2, iconSize, iconSize), accent);
            using (Pen border = new Pen(ModernTheme.Border)) graphics.DrawLine(border, cellBounds.Left, cellBounds.Bottom - 1, cellBounds.Right, cellBounds.Bottom - 1);
        }
    }
    internal sealed class ModernProgress : Control
    {
        private double value;
        internal double Value { get { return value; } set { this.value = value; Invalidate(); } }
        internal Color Accent = ModernTheme.Blue;
        internal ModernProgress() { Height = 10; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; if (Width < 3 || Height < 3) return;
            using (GraphicsPath track = ModernTheme.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2)) using (Brush b = new SolidBrush(ModernTheme.Border)) e.Graphics.FillPath(b, track);
            int width = (int)((Width - 1) * Math.Max(0, Math.Min(1, Value)));
            if (width > 2) using (GraphicsPath fill = ModernTheme.Rounded(new Rectangle(0, 0, width, Height - 1), Height / 2)) using (Brush b = new SolidBrush(Accent)) e.Graphics.FillPath(b, fill);
        }
    }
}
