using System;
using System.Drawing;
using System.Windows.Forms;

namespace GameManagement
{
    // Alert-demo's icon, heading, description and actions recreated in WinForms.
    internal sealed class ModernAlertForm : RetroDialogForm
    {
        private readonly FlowLayoutPanel actions;
        private readonly ModernCard alertCard;
        private readonly ModernIcon alertIcon;
        private readonly Label alertHeading, alertDescription;
        private bool arranging;
        private float arrangedScale;
        internal ModernAlertForm(string title, string description, MessageBoxButtons buttons, MessageBoxIcon kind,
            string affirmativeText = null, string negativeText = null, string cancelText = null)
        {
            Color accent = kind == MessageBoxIcon.Error ? ModernTheme.Error : kind == MessageBoxIcon.Warning ? ModernTheme.Warning : ModernTheme.Blue;
            string glyph = kind == MessageBoxIcon.Error ? "error" : kind == MessageBoxIcon.Warning ? "warning" : "info";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            using (Font measureFont = ModernTheme.Font(10))
            {
                int descriptionHeight = Math.Max(40, Math.Min(224, TextRenderer.MeasureText(description ?? "", measureFont, new Size(400, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + ModernTheme.Space8));
                int cardHeight = ModernTheme.Space24 + ModernTheme.Space32 + ModernTheme.Space12 + descriptionHeight + ModernTheme.Space24;
                ClientSize = new Size(540, cardHeight + ModernTheme.ControlHeight + ModernTheme.Space16 * 3 + ModernTheme.Space24);
                BuildRetroChrome(title);
                alertCard = new ModernCard { Location = new Point(16, 16), Size = new Size(506, cardHeight) };
                alertIcon = new ModernIcon { Glyph = glyph, Accent = accent, Circle = true, Size = new Size(44, 44), Location = new Point(20, 20) };
                alertHeading = ModernTheme.Label(title, 16, true);
                alertHeading.AutoSize = false; alertHeading.AutoEllipsis = true; alertHeading.SetBounds(80, 20, 404, 32);
                alertDescription = ModernTheme.Label(description ?? "", 10);
                alertDescription.AutoSize = false; alertDescription.ForeColor = ModernTheme.Secondary; alertDescription.SetBounds(80, 64, 404, descriptionHeight);
                alertCard.Controls.Add(alertIcon); alertCard.Controls.Add(alertHeading); alertCard.Controls.Add(alertDescription); ContentPanel.Controls.Add(alertCard);
                actions = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = ModernTheme.Background,
                    Location = new Point(16, alertCard.Bottom + ModernTheme.Space16), Size = new Size(506, ModernTheme.ControlHeight + 2), Padding = Padding.Empty };
                ContentPanel.Controls.Add(actions);
                Button primary;
                Button cancel;
                if (buttons == MessageBoxButtons.YesNoCancel)
                {
                    cancel = AddAction(actions, cancelText ?? "Cancel", DialogResult.Cancel, false, accent);
                    AddAction(actions, negativeText ?? "No", DialogResult.No, false, accent);
                    primary = AddAction(actions, affirmativeText ?? "Yes", DialogResult.Yes, true, accent);
                }
                else if (buttons == MessageBoxButtons.YesNo)
                {
                    cancel = AddAction(actions, negativeText ?? "No", DialogResult.No, false, accent);
                    primary = AddAction(actions, affirmativeText ?? "Yes", DialogResult.Yes, true, accent);
                }
                else if (buttons == MessageBoxButtons.OKCancel)
                {
                    cancel = AddAction(actions, cancelText ?? "Cancel", DialogResult.Cancel, false, accent);
                    primary = AddAction(actions, affirmativeText ?? "OK", DialogResult.OK, true, accent);
                }
                else
                {
                    primary = AddAction(actions, affirmativeText ?? "OK", DialogResult.OK, true, accent);
                    cancel = primary;
                }
                AcceptButton = primary; CancelButton = cancel;
                Shown += delegate { ActiveControl = buttons == MessageBoxButtons.OK ? primary : cancel; };
            }
            PerformLayout();
        }

        private static Button AddAction(FlowLayoutPanel actions, string text, DialogResult result, bool primary, Color accent)
        {
            ModernButton button = new ModernButton { Text = text, DialogResult = result, Primary = primary, Accent = text == "Remove" || accent == ModernTheme.Error ? ModernTheme.Error : ModernTheme.Blue,
                AccessibleName = text, Margin = new Padding(8, 0, 0, 0) };
            button.Size = new Size(Math.Max(104, TextRenderer.MeasureText(text, button.Font).Width + 24), ModernTheme.ControlHeight);
            actions.Controls.Add(button); return button;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            actions.BackColor = ModernTheme.Background;
            PerformLayout();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (arranging || actions == null || alertCard == null) return;
            arranging = true;
            try
            {
                float scale = ModernTheme.ScaleFactor(this);
                int textWidth = ModernTheme.Px(this, 404);
                int titleHeight = Math.Max(ModernTheme.Px(this, 32), alertHeading.Font.Height + ModernTheme.Px(this, 4));
                int descriptionHeight = Math.Max(ModernTheme.Px(this, 40), TextRenderer.MeasureText(alertDescription.Text, alertDescription.Font,
                    new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + ModernTheme.Px(this, 8));
                int cardHeight = ModernTheme.Px(this, 20) + titleHeight + ModernTheme.Px(this, 12) + descriptionHeight + ModernTheme.Px(this, 24);
                alertCard.SetBounds(ModernTheme.Px(this, 16), ModernTheme.Px(this, 16), ModernTheme.Px(this, 506), cardHeight);
                alertIcon.SetBounds(ModernTheme.Px(this, 20), ModernTheme.Px(this, 20), ModernTheme.Px(this, 44), ModernTheme.Px(this, 44));
                alertHeading.SetBounds(ModernTheme.Px(this, 80), ModernTheme.Px(this, 20), textWidth, titleHeight);
                alertDescription.SetBounds(ModernTheme.Px(this, 80), alertHeading.Bottom + ModernTheme.Px(this, 12), textWidth, descriptionHeight);
                actions.SetBounds(alertCard.Left, alertCard.Bottom + ModernTheme.Px(this, 16), alertCard.Width, ModernTheme.Px(this, 42));
                foreach (Control action in actions.Controls)
                {
                    action.Size = new Size(Math.Max(ModernTheme.Px(this, 104), TextRenderer.MeasureText(action.Text, action.Font).Width + ModernTheme.Px(this, 24)), ModernTheme.Px(this, 40));
                    action.Margin = new Padding(ModernTheme.Px(this, 8), 0, 0, 0);
                }
                if (Math.Abs(arrangedScale - scale) > .01F)
                {
                    arrangedScale = scale;
                    ClientSize = new Size(ModernTheme.Px(this, 540), actions.Bottom + ModernTheme.Px(this, 60));
                }
                foreach (Control titleBar in Controls)
                {
                    if (titleBar == ContentPanel || titleBar.Dock != DockStyle.Top) continue;
                    titleBar.Height = ModernTheme.Px(this, 44);
                    foreach (Control caption in titleBar.Controls)
                    {
                        if (caption is Button) caption.SetBounds(ClientSize.Width - ModernTheme.Px(this, 40), ModernTheme.Px(this, 7), ModernTheme.Px(this, 32), ModernTheme.Px(this, 32));
                        else caption.Location = new Point(ModernTheme.Px(this, 14), ModernTheme.Px(this, 14));
                    }
                }
            }
            finally { arranging = false; }
        }

        internal static DialogResult Show(IWin32Window owner, string title, string description, MessageBoxButtons buttons = MessageBoxButtons.OK,
            MessageBoxIcon kind = MessageBoxIcon.Information, string affirmativeText = null, string negativeText = null, string cancelText = null)
        {
            using (ModernAlertForm dialog = new ModernAlertForm(title, description, buttons, kind, affirmativeText, negativeText, cancelText))
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        }
    }
}
