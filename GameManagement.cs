using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyVersion("3.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.0.0.0")]

namespace GameManagement
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 1 && string.Equals(args[0], "--break-ui-show", StringComparison.OrdinalIgnoreCase))
            {
                if (!UiSignals.TrySignal(UiSignals.ShowBreakName))
                    RunMainFormSingleInstance(false, true, false);
                return;
            }
            if (args.Length >= 1 && string.Equals(args[0], "--break-ui-hide", StringComparison.OrdinalIgnoreCase))
            {
                UiSignals.TrySignal(UiSignals.HideBreakName);
                return;
            }
            if (args.Length >= 1 && string.Equals(args[0], "--ui-start-hidden", StringComparison.OrdinalIgnoreCase))
            {
                if (!UiSignals.Exists(UiSignals.ShowBreakName))
                    RunMainFormSingleInstance(false, false, true);
                return;
            }
            if (args.Length >= 1 && string.Equals(args[0], "--session-summary", StringComparison.OrdinalIgnoreCase))
            {
                string summaryPath = args.Length >= 2
                    ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-session.json");
                if (File.Exists(summaryPath))
                {
                    try
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        GameSessionSummary summary = serializer.Deserialize<GameSessionSummary>(File.ReadAllText(summaryPath));
                        Application.Run(new SessionSummaryForm(summary));
                    }
                    catch
                    {
                        MessageBox.Show("The saved game summary could not be opened.", "Game Management", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                return;
            }
            if (args.Length >= 2 && string.Equals(args[0], "--render-session-summary", StringComparison.OrdinalIgnoreCase))
            {
                GameSessionSummary preview = new GameSessionSummary
                {
                    Mode = "game",
                    Game = "Example Game",
                    StartedAt = "2026-09-13 13:00:00",
                    EndedAt = "2026-09-13 14:15:00",
                    DurationText = "1 hr 15 min",
                    Cycles = 3,
                    PeakCpu = "Unavailable",
                    PeakGpu = "78°C"
                };
                using (SessionSummaryForm form = new SessionSummaryForm(preview))
                {
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap image = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                        image.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return;
            }
            if (args.Length >= 2 && string.Equals(args[0], "--render-timer-alert", StringComparison.OrdinalIgnoreCase))
            {
                using (TimerAlertForm form = new TimerAlertForm("game-finished", "30", "5", true))
                {
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap image = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                        image.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return;
            }
            if (args.Length >= 1 && string.Equals(args[0], "--timer-alert", StringComparison.OrdinalIgnoreCase))
            {
                string alertType = args.Length >= 2 ? args[1] : "game-finished";
                string finishedMinutes = args.Length >= 3 ? args[2] : "30";
                string nextMinutes = args.Length >= 4 ? args[3] : "5";
                Application.Run(new TimerAlertForm(alertType, finishedMinutes, nextMinutes));
                return;
            }
            if (args.Length >= 2 && string.Equals(args[0], "--render", StringComparison.OrdinalIgnoreCase))
            {
                using (MainForm form = new MainForm(false))
                {
                    if (args.Length >= 4)
                    {
                        int previewWidth;
                        int previewHeight;
                        if (int.TryParse(args[2], out previewWidth) && int.TryParse(args[3], out previewHeight))
                            form.ClientSize = new Size(Math.Max(720, previewWidth), Math.Max(560, previewHeight));
                    }
                    form.Show();
                    Application.DoEvents();
                    if (args.Any(argument => string.Equals(argument, "--show-activity", StringComparison.OrdinalIgnoreCase)))
                    {
                        form.SetActivityVisibleForPreview(true);
                        Application.DoEvents();
                    }
                    if (args.Any(argument => string.Equals(argument, "--hide-activity", StringComparison.OrdinalIgnoreCase)))
                    {
                        form.SetActivityVisibleForPreview(false);
                        Application.DoEvents();
                    }
                    if (args.Any(argument => string.Equals(argument, "--flowers", StringComparison.OrdinalIgnoreCase)))
                    {
                        form.SetFlowerPanelForPreview(true);
                        Application.DoEvents();
                    }
                    if (args.Any(argument => string.Equals(argument, "--work-mode", StringComparison.OrdinalIgnoreCase)))
                    {
                        form.SetWorkViewForPreview(true);
                        Application.DoEvents();
                    }
                    using (Bitmap preview = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                        preview.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return;
            }
            bool enableOnStart = args.Any(argument => string.Equals(argument, "--enable", StringComparison.OrdinalIgnoreCase));
            RunMainFormSingleInstance(enableOnStart, false, false);
        }

        private static void RunMainFormSingleInstance(bool enableOnStart, bool showForBreak, bool hideOnStart)
        {
            bool showCreated;
            bool showBreakCreated;
            bool hideBreakCreated;
            using (System.Threading.EventWaitHandle showSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, UiSignals.ShowMainName, out showCreated))
            using (System.Threading.EventWaitHandle showBreakSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, UiSignals.ShowBreakName, out showBreakCreated))
            using (System.Threading.EventWaitHandle hideBreakSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, UiSignals.HideBreakName, out hideBreakCreated))
            {
                bool createdNew;
                using (System.Threading.Mutex interfaceMutex = new System.Threading.Mutex(true, UiSignals.InterfaceMutexName, out createdNew))
                {
                    if (!createdNew)
                    {
                        if (showForBreak) showBreakSignal.Set();
                        else if (!hideOnStart) showSignal.Set();
                        return;
                    }

                    try { Application.Run(new MainForm(enableOnStart, showForBreak, hideOnStart)); }
                    finally
                    {
                        try { interfaceMutex.ReleaseMutex(); }
                        catch (ApplicationException) { }
                    }
                }
            }
        }
    }

    internal static class UiSignals
    {
        internal const string InterfaceMutexName = @"Local\GameManagementInterface_v1";
        internal const string ShowMainName = @"Local\GameManagementShowMain_v1";
        internal const string ShowBreakName = @"Local\GameManagementShowBreak_v1";
        internal const string HideBreakName = @"Local\GameManagementHideBreak_v1";

        internal static bool TrySignal(string name)
        {
            try
            {
                using (System.Threading.EventWaitHandle signal = System.Threading.EventWaitHandle.OpenExisting(name))
                {
                    return signal.Set();
                }
            }
            catch (System.Threading.WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static bool Exists(string name)
        {
            try
            {
                using (System.Threading.EventWaitHandle signal = System.Threading.EventWaitHandle.OpenExisting(name)) { return true; }
            }
            catch (System.Threading.WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }

    internal sealed class RetroDialogButton : Button
    {
        private bool pressed;

        public RetroDialogButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            FlatStyle = FlatStyle.Standard;
            UseVisualStyleBackColor = false;
            BackColor = Color.FromArgb(192, 192, 192);
            ForeColor = Color.Black;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle bounds = ClientRectangle;
            e.Graphics.Clear(Enabled ? BackColor : Color.FromArgb(192, 192, 192));
            ControlPaint.DrawBorder3D(e.Graphics, bounds, pressed ? Border3DStyle.Sunken : Border3DStyle.Raised);
            Rectangle textBounds = new Rectangle(
                bounds.X + (pressed ? 2 : 1),
                bounds.Y + (pressed ? 2 : 1),
                Math.Max(0, bounds.Width - 3),
                Math.Max(0, bounds.Height - 3));
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                textBounds,
                Enabled ? ForeColor : SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseCaptureChanged(e);
        }
    }

    internal abstract class RetroDialogForm : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        protected Panel ContentPanel { get; private set; }

        protected void BuildRetroChrome(string caption)
        {
            Text = caption;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(192, 192, 192);
            Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(3);

            Panel titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(0, 0, 128)
            };
            titleBar.MouseDown += DragWindow;

            Label title = new Label
            {
                Text = "▣  " + caption,
                ForeColor = Color.White,
                Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(5, 6)
            };
            title.MouseDown += DragWindow;
            titleBar.Controls.Add(title);

            Button close = RetroButton("×", 24, 21);
            close.Dock = DockStyle.Right;
            close.TabStop = false;
            close.Click += delegate { Close(); };
            titleBar.Controls.Add(close);
            Controls.Add(titleBar);

            ContentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(192, 192, 192)
            };
            Controls.Add(ContentPanel);
            ContentPanel.BringToFront();
        }

        protected static Button RetroButton(string text, int width, int height)
        {
            return new RetroDialogButton
            {
                Text = text,
                Size = new Size(width, height),
                Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular)
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Raised);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }
    }

    internal sealed class TimerAlertForm : RetroDialogForm
    {
        private readonly Timer soundTimer = new Timer();
        private readonly bool previewOnly;

        public TimerAlertForm(string alertType, string finishedMinutes, string nextMinutes, bool previewOnly = false)
        {
            this.previewOnly = previewOnly;
            bool breakFinished = string.Equals(alertType, "break-finished", StringComparison.OrdinalIgnoreCase);
            bool workAlert = alertType.StartsWith("work-", StringComparison.OrdinalIgnoreCase);
            breakFinished = breakFinished || string.Equals(alertType, "work-break-finished", StringComparison.OrdinalIgnoreCase);
            string heading = breakFinished ? "Break over!" : "Time for a break!";
            string timerName = breakFinished ? (workAlert ? "Work timer" : "Game timer") : "Break timer";
            string message = breakFinished ? (workAlert ? "You can work again." : "You can play again.") : (workAlert ? "Your work time is up." : "Your game time is up.");

            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 230);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            BuildRetroChrome("Game Management Alarm");

            Label headingLabel = new Label
            {
                AutoSize = false,
                Location = new Point(22, 18),
                Size = new Size(390, 31),
                Font = new Font("Arial", 17F, FontStyle.Bold),
                ForeColor = Color.Black,
                Text = heading
            };

            Panel messagePanel = new Panel
            {
                Location = new Point(21, 56),
                Size = new Size(396, 76),
                BackColor = Color.FromArgb(255, 255, 255),
                BorderStyle = BorderStyle.Fixed3D
            };

            Label warningIcon = new Label
            {
                Text = "!",
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(12, 17),
                Size = new Size(35, 35),
                BackColor = Color.Yellow,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Arial", 18F, FontStyle.Bold)
            };
            messagePanel.Controls.Add(warningIcon);

            Label messageLabel = new Label
            {
                AutoSize = false,
                Location = new Point(61, 15),
                Size = new Size(315, 48),
                Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Regular),
                ForeColor = Color.Black,
                Text = message + Environment.NewLine + timerName + ": " + FormatDuration(nextMinutes) + "."
            };
            messagePanel.Controls.Add(messageLabel);

            Button stopButton = RetroButton("Stop alarm", 104, 25);
            stopButton.Location = new Point(313, 151);
            stopButton.Click += delegate { Close(); };

            Label status = new Label
            {
                Text = "Alarm is sounding",
                BorderStyle = BorderStyle.Fixed3D,
                Location = new Point(21, 154),
                Size = new Size(275, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            ContentPanel.Controls.Add(headingLabel);
            ContentPanel.Controls.Add(messagePanel);
            ContentPanel.Controls.Add(status);
            ContentPanel.Controls.Add(stopButton);
            CancelButton = stopButton;

            soundTimer.Interval = 1500;
            soundTimer.Tick += delegate { PlayAlarmSound(); };
            Shown += delegate
            {
                if (!this.previewOnly)
                {
                    PlayAlarmSound();
                    soundTimer.Start();
                }
                Activate();
                ActiveControl = null;
            };
            FormClosed += delegate
            {
                soundTimer.Stop();
                soundTimer.Dispose();
            };
        }

        private static string FormatDuration(string minutesText)
        {
            decimal minutes;
            if (!decimal.TryParse(minutesText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out minutes))
                return minutesText + " minutes";

            if (minutes > 0 && minutes < 1)
            {
                int seconds = (int)Math.Round(minutes * 60, MidpointRounding.AwayFromZero);
                return seconds == 1 ? "1 second" : seconds + " seconds";
            }

            decimal rounded = decimal.Round(minutes, 2);
            return rounded == 1 ? "1 minute" : rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " minutes";
        }

        private static void PlayAlarmSound()
        {
            try
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch
            {
                // The alert stays visible if Windows cannot play a system sound.
            }
        }
    }

    internal sealed class GameSettings
    {
        public int pollSeconds { get; set; }
        public int gameBrightnessPercent { get; set; }
        public bool gameTimerEnabled { get; set; }
        public int gameTimerMinutes { get; set; }
        public int breakTimerMinutes { get; set; }
        public bool workTimerEnabled { get; set; }
        public int workTimerMinutes { get; set; }
        public int workBreakMinutes { get; set; }
        public bool pauseGameWithEscape { get; set; }
        public string[] gameApps { get; set; }
        public string[] gameFolders { get; set; }
        public string[] excludedProcesses { get; set; }
        public string activeMode { get; set; }
        public string[] workApps { get; set; }
    }

    internal sealed class GameSessionSummary
    {
        public string Mode { get; set; }
        public string Game { get; set; }
        public string StartedAt { get; set; }
        public string EndedAt { get; set; }
        public string DurationText { get; set; }
        public int Cycles { get; set; }
        public string PeakCpu { get; set; }
        public string PeakGpu { get; set; }
    }

    internal sealed class SessionSummaryForm : RetroDialogForm
    {
        public SessionSummaryForm(GameSessionSummary summary)
        {
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(490, 370);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BuildRetroChrome("Game Management - Session saved");

            bool workSession = string.Equals(summary.Mode, "work", StringComparison.OrdinalIgnoreCase);
            Label heading = new Label
            {
                Text = workSession ? "WORK SESSION FINISHED" : "GAME SESSION FINISHED",
                Location = new Point(20, 14),
                Size = new Size(445, 31),
                Font = new Font("Arial", 17F, FontStyle.Bold),
                ForeColor = Color.Black
            };
            ContentPanel.Controls.Add(heading);

            string details =
                (workSession ? "Applications: " : "Game: ") + Safe(summary.Game) + Environment.NewLine +
                (workSession ? "Worked: " : "Played: ") + Safe(summary.DurationText) + Environment.NewLine +
                "Timer cycles: " + summary.Cycles + Environment.NewLine +
                "Peak CPU temp: " + Safe(summary.PeakCpu) + Environment.NewLine +
                "Peak GPU temp: " + Safe(summary.PeakGpu) + Environment.NewLine +
                "Started: " + Safe(summary.StartedAt) + Environment.NewLine +
                "Finished: " + Safe(summary.EndedAt);

            GroupBox detailsGroup = new GroupBox
            {
                Text = "Session summary",
                Location = new Point(20, 51),
                Size = new Size(445, 221)
            };
            ContentPanel.Controls.Add(detailsGroup);

            Label detailsLabel = new Label
            {
                Text = details,
                Location = new Point(15, 24),
                Size = new Size(410, 178),
                Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Regular),
                ForeColor = Color.Black
            };
            detailsGroup.Controls.Add(detailsLabel);

            Label savedLabel = new Label
            {
                Text = "Saved in GameSessionHistory.txt",
                Location = new Point(20, 287),
                Size = new Size(320, 20),
                BorderStyle = BorderStyle.Fixed3D,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Black,
                Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular)
            };
            ContentPanel.Controls.Add(savedLabel);

            Button closeButton = RetroButton("Close", 104, 25);
            closeButton.Location = new Point(361, 284);
            closeButton.Click += delegate { Close(); };
            closeButton.DialogResult = DialogResult.Cancel;
            ContentPanel.Controls.Add(closeButton);
            CancelButton = closeButton;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
        }
    }

    internal sealed class PixelGerberaPanel : Control
    {
        private readonly Timer bloomTimer = new Timer();
        private int frame;

        public PixelGerberaPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(0, 128, 128);
            bloomTimer.Interval = 90;
            bloomTimer.Tick += delegate
            {
                frame = (frame + 1) % 720;
                Invalidate();
            };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) bloomTimer.Start(); else bloomTimer.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            graphics.Clear(Color.FromArgb(0, 128, 128));
            ControlPaint.DrawBorder3D(graphics, ClientRectangle, Border3DStyle.Sunken);

            Rectangle garden = new Rectangle(5, 5, Math.Max(1, ClientSize.Width - 10), Math.Max(1, ClientSize.Height - 10));
            using (Brush sky = new SolidBrush(Color.FromArgb(0, 128, 128))) graphics.FillRectangle(sky, garden);

            int baseY = garden.Bottom - 7;
            int leftX = garden.Left + garden.Width * 28 / 100;
            int middleX = garden.Left + garden.Width * 50 / 100;
            int rightX = garden.Left + garden.Width * 72 / 100;
            int leftY = garden.Top + garden.Height * 43 / 100;
            int middleY = garden.Top + garden.Height * 28 / 100;
            int rightY = garden.Top + garden.Height * 48 / 100;
            int leftSway = PixelSway(0);
            int middleSway = PixelSway(16);
            int rightSway = PixelSway(32);

            DrawStem(graphics, garden.Left + garden.Width * 44 / 100, baseY, leftX + leftSway, leftY + 8, Color.FromArgb(0, 192, 96));
            DrawStem(graphics, garden.Left + garden.Width * 49 / 100, baseY, middleX + middleSway, middleY + 8, Color.FromArgb(0, 224, 96));
            DrawStem(graphics, garden.Left + garden.Width * 55 / 100, baseY, rightX + rightSway, rightY + 8, Color.FromArgb(0, 160, 80));
            DrawBloom(graphics, leftX + leftSway, leftY, Color.FromArgb(255, 128, 32), Color.FromArgb(192, 64, 16), Color.FromArgb(255, 224, 64), 0);
            DrawBloom(graphics, middleX + middleSway, middleY, Color.FromArgb(255, 224, 32), Color.FromArgb(224, 160, 0), Color.FromArgb(128, 64, 32), 5);
            DrawBloom(graphics, rightX + rightSway, rightY, Color.FromArgb(240, 96, 96), Color.FromArgb(176, 48, 64), Color.FromArgb(255, 192, 64), 10);

        }

        private int PixelSway(int offset)
        {
            return (int)Math.Round(Math.Sin((frame + offset) * 0.08)) * 3;
        }

        private static void DrawStem(Graphics graphics, int startX, int startY, int endX, int endY, Color color)
        {
            const int pixel = 3;
            int steps = Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY)) / pixel;
            steps = Math.Max(1, steps);
            using (Brush brush = new SolidBrush(color))
            using (Brush leaf = new SolidBrush(Color.FromArgb(0, 112, 64)))
            {
                for (int step = 0; step <= steps; step++)
                {
                    int x = startX + (endX - startX) * step / steps;
                    int y = startY + (endY - startY) * step / steps;
                    graphics.FillRectangle(brush, x, y, pixel, pixel);
                    if (step == steps / 2)
                    {
                        graphics.FillRectangle(leaf, x - 6, y - 3, 6, 3);
                        graphics.FillRectangle(leaf, x - 9, y - 6, 6, 3);
                    }
                }
            }
        }

        private void DrawBloom(Graphics graphics, int centerX, int centerY, Color bright, Color dark, Color center, int offset)
        {
            const int pixel = 4;
            int pulse = ((frame + offset) / 6) % 2;
            using (Brush brightBrush = new SolidBrush(bright))
            using (Brush darkBrush = new SolidBrush(dark))
            using (Brush centerBrush = new SolidBrush(center))
            {
                for (int petal = 0; petal < 16; petal++)
                {
                    double angle = petal * Math.PI * 2.0 / 16.0;
                    int radiusLimit = 4 + ((petal + pulse) % 3 == 0 ? 1 : 0);
                    for (int radius = 2; radius <= radiusLimit; radius++)
                    {
                        int x = centerX + (int)Math.Round(Math.Cos(angle) * radius) * pixel;
                        int y = centerY + (int)Math.Round(Math.Sin(angle) * radius) * pixel;
                        graphics.FillRectangle(radius == radiusLimit ? darkBrush : brightBrush, x - 2, y - 2, pixel, pixel);
                    }
                }
                graphics.FillRectangle(darkBrush, centerX - 6, centerY - 6, 12, 12);
                graphics.FillRectangle(centerBrush, centerX - 3, centerY - 3, 6, 6);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) bloomTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class InstalledApp
    {
        public string Name;
        public string Path;
    }

    internal sealed class GameDetectionTarget
    {
        public string Path { get; private set; }
        public bool IsApplication { get; private set; }

        public GameDetectionTarget(string path, bool isApplication)
        {
            Path = path;
            IsApplication = isApplication;
        }

        public override string ToString()
        {
            return (IsApplication ? "[App] " : "[Folder] ") + Path;
        }
    }

    internal sealed class ManagementStatusSnapshot
    {
        public int Generation;
        public bool Full;
        public bool Watcher;
        public bool StateActive;
        public bool Managed;
        public bool Startup;
        public bool Restarting;
        public string ActivePlan;
        public string Game;
        public int? Brightness;
        public string[] WorkApplications = new string[0];
        public DateTime? WorkSessionStartedAt;
        public string[] RecentLogLines;
        public string Error;
    }

    internal sealed class InstalledAppsForm : RetroDialogForm
    {
        private static readonly object DiscoveryCacheLock = new object();
        private static List<InstalledApp> cachedApplications;
        private readonly ListView list = new ListView();
        private readonly TextBox search = new TextBox();
        private readonly Button addButton;
        private List<InstalledApp> applications = new List<InstalledApp>();
        private readonly ImageList icons = new ImageList();
        private int fillGeneration;
        private bool discoveryStarted;
        public string SelectedPath { get; private set; }

        public InstalledAppsForm()
        {
            ClientSize = new Size(600, 460);
            StartPosition = FormStartPosition.CenterParent;
            BuildRetroChrome("Choose installed application");
            Label help = new Label { Text = "Select an app. Browse file... is available for anything missing here.", Location = new Point(15, 13), AutoSize = true };
            ContentPanel.Controls.Add(help);
            search.Location = new Point(15, 37);
            search.Width = 565;
            search.Enabled = false;
            search.TextChanged += delegate { FillList(); };
            ContentPanel.Controls.Add(search);
            list.Location = new Point(15, 68);
            list.Size = new Size(565, 310);
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.Columns.Add("Application", 220);
            list.Columns.Add("Executable", 320);
            icons.ImageSize = new Size(16, 16);
            list.SmallImageList = icons;
            list.DoubleClick += delegate { SelectApp(); };
            ContentPanel.Controls.Add(list);
            addButton = RetroButton("Add selected", 105, 25);
            addButton.Location = new Point(475, 389);
            addButton.TabStop = true;
            addButton.Enabled = false;
            addButton.Click += delegate { SelectApp(); };
            ContentPanel.Controls.Add(addButton);
            AcceptButton = addButton;
            list.Items.Add(new ListViewItem("Loading installed applications...") { ForeColor = Color.DimGray });
            Shown += delegate { BeginDiscovery(); };
            FormClosed += delegate { System.Threading.Interlocked.Increment(ref fillGeneration); };
        }

        private void BeginDiscovery()
        {
            if (discoveryStarted) return;
            discoveryStarted = true;
            System.Threading.Thread discoveryThread = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                List<InstalledApp> discovered = DiscoverCached();
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        if (IsDisposed) return;
                        applications = discovered;
                        search.Enabled = true;
                        addButton.Enabled = true;
                        FillList();
                        search.Focus();
                    }));
                }
                catch (InvalidOperationException) { }
            }));
            discoveryThread.IsBackground = true;
            discoveryThread.Name = "Game Management installed-app discovery";
            discoveryThread.SetApartmentState(System.Threading.ApartmentState.STA);
            discoveryThread.Start();
        }

        private void FillList()
        {
            int generation = System.Threading.Interlocked.Increment(ref fillGeneration);
            List<KeyValuePair<ListViewItem, string>> rows = new List<KeyValuePair<ListViewItem, string>>();
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                icons.Images.Clear();
                foreach (InstalledApp app in applications.Where(item => item.Name.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0 || item.Path.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    ListViewItem row = new ListViewItem(app.Name, -1);
                    row.SubItems.Add(app.Path);
                    row.Tag = app.Path;
                    list.Items.Add(row);
                    rows.Add(new KeyValuePair<ListViewItem, string>(row, app.Path));
                }
            }
            finally { list.EndUpdate(); }
            LoadIconsInBackground(rows, generation);
        }

        private void LoadIconsInBackground(List<KeyValuePair<ListViewItem, string>> rows, int generation)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (KeyValuePair<ListViewItem, string> entry in rows)
                {
                    if (generation != System.Threading.Interlocked.CompareExchange(ref fillGeneration, 0, 0)) return;
                    ListViewItem row = entry.Key;
                    string path = entry.Value;
                    Bitmap iconBitmap = null;
                    try
                    {
                        using (Icon icon = Icon.ExtractAssociatedIcon(path))
                            if (icon != null) iconBitmap = icon.ToBitmap();
                    }
                    catch { }
                    if (iconBitmap == null) continue;
                    Bitmap readyBitmap = iconBitmap;
                    try
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            try
                            {
                                if (generation != fillGeneration || IsDisposed || row.ListView != list) return;
                                icons.Images.Add(readyBitmap);
                                row.ImageIndex = icons.Images.Count - 1;
                            }
                            finally { readyBitmap.Dispose(); }
                        }));
                    }
                    catch
                    {
                        readyBitmap.Dispose();
                        return;
                    }
                }
            });
        }

        private void SelectApp()
        {
            if (list.SelectedItems.Count == 0) return;
            string path = Convert.ToString(list.SelectedItems[0].Tag);
            if (!File.Exists(path)) { MessageBox.Show(this, "That application is no longer available. Try Browse file...", "Game Management"); return; }
            SelectedPath = path;
            DialogResult = DialogResult.OK;
            Close();
        }

        internal static List<InstalledApp> Discover()
        {
            Dictionary<string, InstalledApp> found = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey paths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                    {
                        if (paths == null) continue;
                        foreach (string name in paths.GetSubKeyNames())
                        {
                            using (RegistryKey entry = paths.OpenSubKey(name))
                                AddCandidate(found, Path.GetFileNameWithoutExtension(name), Convert.ToString(entry == null ? null : entry.GetValue(null)));
                        }
                    }
                }
                catch { }
            }
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                object shell = null;
                try
                {
                    shell = Activator.CreateInstance(shellType);
                    foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                    {
                        string[] shortcuts;
                        try { shortcuts = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories); }
                        catch { continue; }
                        foreach (string shortcutPath in shortcuts)
                        {
                            object shortcut = null;
                            try
                            {
                                shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                                string target = Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
                                string arguments = Convert.ToString(shortcut.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null));
                                if (string.IsNullOrWhiteSpace(arguments)) AddCandidate(found, Path.GetFileNameWithoutExtension(shortcutPath), target);
                            }
                            catch { }
                            finally { if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut); }
                        }
                    }
                }
                catch { }
                finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
            }
            return found.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static List<InstalledApp> DiscoverCached()
        {
            lock (DiscoveryCacheLock)
            {
                if (cachedApplications != null) return new List<InstalledApp>(cachedApplications);
            }
            List<InstalledApp> discovered = Discover();
            lock (DiscoveryCacheLock)
            {
                if (cachedApplications == null) cachedApplications = discovered;
                return new List<InstalledApp>(cachedApplications);
            }
        }

        private static void AddCandidate(Dictionary<string, InstalledApp> found, string name, string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return;
            string path = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
            string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(windowsFolder, StringComparison.OrdinalIgnoreCase)) return;
            if (path.IndexOf(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(@"\Windows Mail\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(@"\Common Files\microsoft shared\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(@"\Internet Explorer\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(path, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameManagement.exe"), StringComparison.OrdinalIgnoreCase)) return;
            string label = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name.Trim();
            if (string.Equals(Path.GetFileName(path), "DesktopOverlayHost.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(path), "nahimicNotifSys.exe", StringComparison.OrdinalIgnoreCase) ||
                System.Text.RegularExpressions.Regex.IsMatch(label + " " + Path.GetFileName(path), @"(^|[\s_-])(uninstall|unins\d*|update|updater|helper|crash|setup|installer|stackbuilder|diagnostic|bootstrapper|pg_ctl)([\s_.-]|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return;
            if (string.Equals(label, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description)) label = description.Trim();
                }
                catch { }
            }
            InstalledApp existing;
            if (found.TryGetValue(path, out existing))
            {
                if (label.Length > existing.Name.Length) existing.Name = label;
            }
            else found.Add(path, new InstalledApp { Name = label, Path = path });
        }
    }

    internal sealed class TrackedSession
    {
        public string Mode;
        public string Application;
        public DateTime Start;
        public DateTime End;
    }

    internal static class WeeklyReport
    {
        internal static List<TrackedSession> Load(string path)
        {
            List<TrackedSession> sessions = new List<TrackedSession>();
            if (!File.Exists(path)) return sessions;
            string mode = "game", application = "";
            DateTime start = DateTime.MinValue, end = DateTime.MinValue;
            Action flush = delegate
            {
                if (start != DateTime.MinValue && end > start)
                    sessions.Add(new TrackedSession { Mode = mode, Application = application, Start = start, End = end });
                mode = "game"; application = ""; start = end = DateTime.MinValue;
            };
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("----", StringComparison.Ordinal)) { flush(); continue; }
                int separator = line.IndexOf(':');
                if (separator < 0) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key == "Mode") mode = string.Equals(value, "work", StringComparison.OrdinalIgnoreCase) ? "work" : "game";
                else if (key == "Applications" || key == "Game") application = value;
                else if (key == "Started") start = ParseHistoryTime(value);
                else if (key == "Finished") end = ParseHistoryTime(value);
            }
            flush();
            return sessions;
        }

        private static DateTime ParseHistoryTime(string value)
        {
            DateTime parsed;
            if (DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed)) return parsed;
            return DateTime.TryParse(value, out parsed) ? parsed : DateTime.MinValue;
        }

        internal static string Build(IEnumerable<TrackedSession> sessions, DateTime weekStart)
        {
            DateTime beginning = weekStart.Date;
            DateTime ending = beginning.AddDays(7);
            TimeSpan[] game = new TimeSpan[7], work = new TimeSpan[7];
            Dictionary<string, TimeSpan> gameApps = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TimeSpan> workApps = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            int gameSessions = 0, workSessions = 0;
            int unattributed = 0;
            foreach (TrackedSession session in sessions)
            {
                if (session == null || session.End <= session.Start || session.End <= beginning || session.Start >= ending) continue;
                bool isWork = string.Equals(session.Mode, "work", StringComparison.OrdinalIgnoreCase);
                if (isWork) workSessions++; else gameSessions++;
                DateTime cursor = session.Start > beginning ? session.Start : beginning;
                DateTime stop = session.End < ending ? session.End : ending;
                TimeSpan weeklyDuration = stop - cursor;
                if (string.IsNullOrWhiteSpace(session.Application) || session.Application.Contains(",")) unattributed++;
                else
                {
                    Dictionary<string, TimeSpan> apps = isWork ? workApps : gameApps;
                    TimeSpan prior;
                    apps.TryGetValue(session.Application, out prior);
                    apps[session.Application] = prior + weeklyDuration;
                }
                while (cursor < stop)
                {
                    int day = (int)(cursor.Date - beginning).TotalDays;
                    DateTime dayEnd = cursor.Date.AddDays(1);
                    DateTime sliceEnd = stop < dayEnd ? stop : dayEnd;
                    if (isWork) work[day] += sliceEnd - cursor; else game[day] += sliceEnd - cursor;
                    cursor = sliceEnd;
                }
            }
            StringBuilder report = new StringBuilder();
            report.AppendLine("Week of " + beginning.ToString("yyyy-MM-dd") + " to " + ending.AddDays(-1).ToString("yyyy-MM-dd"));
            report.AppendLine();
            for (int day = 0; day < 7; day++)
                report.AppendLine(beginning.AddDays(day).ToString("dddd, MMM d") + "    Game " + Format(game[day]) + "    Work " + Format(work[day]) + "    Total " + Format(game[day] + work[day]));
            TimeSpan gameTotal = TimeSpan.FromTicks(game.Sum(item => item.Ticks));
            TimeSpan workTotal = TimeSpan.FromTicks(work.Sum(item => item.Ticks));
            report.AppendLine().AppendLine("WEEKLY SUMMARY");
            report.AppendLine("Game Mode: " + Format(gameTotal) + " (" + gameSessions + (gameSessions == 1 ? " session)" : " sessions)"));
            report.AppendLine("Work Mode: " + Format(workTotal) + " (" + workSessions + (workSessions == 1 ? " session)" : " sessions)"));
            report.AppendLine("Total tracked time: " + Format(gameTotal + workTotal));
            AppendApplications(report, "Games", gameApps);
            AppendApplications(report, "Work applications", workApps);
            if (gameApps.Count > 0) report.AppendLine("Most used game: " + gameApps.OrderByDescending(item => item.Value).First().Key);
            if (workApps.Count > 0) report.AppendLine("Most used work app: " + workApps.OrderByDescending(item => item.Value).First().Key);
            if (unattributed > 0) report.AppendLine().AppendLine(unattributed + " sessions used multiple or unknown apps; their time is included in mode totals but not assigned to an individual app.");
            report.AppendLine().AppendLine("Sessions of two minutes or less were not saved by the existing history rule.");
            return report.ToString();
        }

        private static void AppendApplications(StringBuilder report, string label, Dictionary<string, TimeSpan> apps)
        {
            report.AppendLine().AppendLine(label + ":");
            if (apps.Count == 0) { report.AppendLine("No individually attributed time."); return; }
            foreach (var app in apps.OrderByDescending(item => item.Value)) report.AppendLine("  " + app.Key + "  " + Format(app.Value));
        }

        private static string Format(TimeSpan duration)
        {
            return ((int)duration.TotalHours) + "h " + duration.Minutes.ToString("00") + "m";
        }
    }

    internal sealed class WeeklyReportForm : RetroDialogForm
    {
        public WeeklyReportForm(string historyPath)
        {
            ClientSize = new Size(650, 570);
            StartPosition = FormStartPosition.CenterParent;
            BuildRetroChrome("Weekly Report");
            DateTime today = DateTime.Today;
            DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            TextBox text = new TextBox { Location = new Point(15, 14), Size = new Size(620, 474), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Lucida Console", 9F), BackColor = Color.White };
            try { text.Text = WeeklyReport.Build(WeeklyReport.Load(historyPath), monday); }
            catch (Exception ex) { text.Text = "The weekly report could not be read: " + ex.Message; }
            ContentPanel.Controls.Add(text);
            Button close = RetroButton("Close", 92, 25);
            close.Location = new Point(543, 498);
            close.Click += delegate { Close(); };
            ContentPanel.Controls.Add(close);
        }
    }

    internal sealed class MainForm : Form
    {
        private const string ManagementPlanGuid = "c8b1a303-89f5-4b03-ae3f-10b46a186527";
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        private const int WsMinimizeBox = 0x00020000;
        private const int WsSysMenu = 0x00080000;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private readonly string rootPath;
        private readonly string settingsPath;
        private readonly string logPath;
        private readonly string statePath;
        private readonly string timerStatePath;
        private readonly string workTimerStatePath;
        private readonly string historyPath;
        private readonly string performanceStatePath;
        private readonly string engineEnabledPath;
        private readonly string watcherPath;
        private readonly string installerPath;
        private readonly string pausePath;

        private readonly Timer refreshTimer = new Timer();
        private readonly Timer countdownTimer = new Timer();
        private readonly Timer performanceTimer = new Timer();
        private readonly Timer panelFlipTimer = new Timer();
        private readonly Timer workspaceFlipTimer = new Timer();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly bool enableOnStart;
        private readonly bool showForBreakOnStart;
        private readonly bool hideOnStart;
        private System.Threading.EventWaitHandle showBreakEvent;
        private System.Threading.EventWaitHandle hideBreakEvent;
        private System.Threading.EventWaitHandle showMainEvent;
        private System.Threading.SynchronizationContext uiContext;
        private System.Threading.Thread uiSignalThread;
        private volatile bool stopUiSignalThread;
        private float? peakCpuTemperature;
        private float? peakGpuTemperature;
        private bool managementStateKnown;
        private bool previousManagementState;
        private DateTime lastWatcherStartAttempt = DateTime.MinValue;
        private bool modeSwitchInProgress;
        private string temperatureSessionKey;
        private int temperatureRefreshRunning;
        private int statusRefreshRunning;
        private int statusRefreshPending;
        private int statusRefreshGeneration;
        private bool settingsSaveInProgress;
        private int controlActionRunning;
        private int watcherTransitionRunning;
        private bool exiting;
        private GameSettings settings;

        private Label statusValue;
        private Label gameValue;
        private Label planValue;
        private Label brightnessValue;
        private Label timerValue;
        private Label minutesLeftValue;
        private Label cycleValue;
        private Label cpuTemperatureValue;
        private Label gpuTemperatureValue;
        private Label watcherValue;
        private Panel statusLamp;
        private GroupBox statusGroup;
        private Panel statusSurface;
        private Panel statusFrontPanel;
        private PixelGerberaPanel flowerPanel;
        private Button flipStatusButton;
        private Button flowerFlipButton;
        private NumericUpDown brightnessInput;
        private NumericUpDown pollInput;
        private NumericUpDown timerMinutesInput;
        private NumericUpDown breakMinutesInput;
        private CheckBox timerEnabledCheck;
        private CheckBox pauseWithEscapeCheck;
        private CheckBox startupCheck;
        private CheckBox trayCheck;
        private ListBox foldersList;
        private TextBox logBox;
        private GroupBox logGroup;
        private Button enableButton;
        private Button pauseButton;
        private Button toggleLogButton;
        private Button confirmTimerButton;
        private Label footerLabel;
        private Panel gameSurface;
        private Panel workSurface;
        private Button modeFlipButton;
        private Button workModeFlipButton;
        private ListBox workAppsList;
        private Label workModeValue;
        private Label workAppsValue;
        private Label workElapsedValue;
        private Label workCpuValue;
        private Label workGpuValue;
        private Label workFooterLabel;
        private Label workTimerPhaseValue;
        private Label workMinutesLeftValue;
        private Label workCycleValue;
        private CheckBox workTimerEnabledCheck;
        private NumericUpDown workTimerMinutesInput;
        private NumericUpDown workBreakMinutesInput;
        private DateTime? workSessionStartedAt;
        private bool workView;
        private bool targetWorkView;
        private int workspaceFlipFrame;
        private Rectangle workspaceFlipBounds;
        private Panel workspaceFlipSurface;
        private bool activityVisible = false;
        private const int CompactExpandedHeight = 665;
        private int expandedClientHeight = CompactExpandedHeight;
        private bool showingFlowers;
        private bool panelFlipping;
        private int panelFlipFrame;
        private Rectangle panelFlipBounds;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style |= WsMinimizeBox | WsSysMenu;
                return parameters;
            }
        }

        public MainForm(bool enableOnStart, bool showForBreakOnStart = false, bool hideOnStart = false)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            this.enableOnStart = enableOnStart;
            this.showForBreakOnStart = showForBreakOnStart;
            this.hideOnStart = hideOnStart;
            string documentsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GameManagement");
            rootPath = File.Exists(Path.Combine(documentsRoot, "GameManagement.ps1"))
                ? documentsRoot
                : AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            settingsPath = Path.Combine(rootPath, "settings.json");
            logPath = Path.Combine(rootPath, "GameManagement.log");
            statePath = Path.Combine(rootPath, "runtime-state.json");
            timerStatePath = Path.Combine(rootPath, "timer-state.json");
            workTimerStatePath = Path.Combine(rootPath, "work-timer-state.json");
            historyPath = Path.Combine(rootPath, "GameSessionHistory.txt");
            performanceStatePath = Path.Combine(rootPath, "performance-state.json");
            engineEnabledPath = Path.Combine(rootPath, "engine-enabled.flag");
            watcherPath = Path.Combine(rootPath, "GameManagement.ps1");
            installerPath = Path.Combine(rootPath, "Install-GameManagement.ps1");
            pausePath = Path.Combine(rootPath, "Pause-GameManagement.ps1");

            BuildWindow();
            BuildTrayIcon();
            InitializeUiSignals();
            LoadSettings();
            RefreshStatus();

            refreshTimer.Interval = 3000;
            refreshTimer.Tick += delegate { RefreshStatus(); };
            refreshTimer.Start();

            countdownTimer.Interval = 250;
            countdownTimer.Tick += delegate
            {
                if (!Visible || modeSwitchInProgress) return;
                if (workView) { RefreshWorkElapsedDisplay(); RefreshWorkTimerDisplay(); }
                else RefreshTimerDisplay(IsRuntimeMode("game"));
            };
            countdownTimer.Start();

            performanceTimer.Interval = 1000;
            performanceTimer.Tick += delegate { RefreshTemperatureMetrics(); };
            performanceTimer.Start();
            RefreshTemperatureMetrics();

        }

        private void BuildWindow()
        {
            Text = "Game Management";
            ClientSize = new Size(820, 570);
            MinimumSize = new Size(720, 570);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(192, 192, 192);
            Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(3);
            ShowInTaskbar = true;
            Icon = LoadApplicationIcon();

            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 28;
            titleBar.BackColor = Color.FromArgb(0, 0, 128);
            titleBar.MouseDown += DragWindow;

            Label title = new Label();
            title.Text = "▣  Game Management";
            title.ForeColor = Color.White;
            title.Font = new Font("Microsoft Sans Serif", 9F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(5, 6);
            title.MouseDown += DragWindow;
            titleBar.Controls.Add(title);

            Button minimize = RetroButton("_", 24, 21);
            minimize.Dock = DockStyle.Right;
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            titleBar.Controls.Add(minimize);

            Button help = RetroButton("?", 24, 21);
            help.Dock = DockStyle.Right;
            help.TabStop = false;
            titleBar.Controls.Add(help);

            Button close = RetroButton("×", 24, 21);
            close.Dock = DockStyle.Right;
            close.Click += delegate { HideToTray(); };
            titleBar.Controls.Add(close);
            Controls.Add(titleBar);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(16, 12, 16, 13);
            Controls.Add(body);
            body.BringToFront();

            gameSurface = new Panel();
            gameSurface.Location = Point.Empty;
            gameSurface.Size = body.ClientSize;
            gameSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            gameSurface.BackColor = Color.FromArgb(192, 192, 192);
            body.Controls.Add(gameSurface);

            workSurface = new Panel();
            workSurface.Location = Point.Empty;
            workSurface.Size = body.ClientSize;
            workSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            workSurface.BackColor = Color.FromArgb(192, 192, 192);
            workSurface.Visible = false;
            body.Controls.Add(workSurface);
            gameSurface.BringToFront();

            Label brand = new Label();
            brand.Text = "GAME MANAGEMENT";
            brand.Font = new Font("Arial", 26F, FontStyle.Bold);
            brand.AutoSize = true;
            brand.Location = new Point(17, 12);
            gameSurface.Controls.Add(brand);

            Label subtitle = new Label();
            subtitle.Text = "Automatic game management and break controller";
            subtitle.AutoSize = true;
            subtitle.Location = new Point(21, 53);
            gameSurface.Controls.Add(subtitle);

            statusGroup = RetroGroup("Current status", 20, 82, 375, 248);
            gameSurface.Controls.Add(statusGroup);

            statusSurface = new Panel();
            statusSurface.Location = new Point(3, 15);
            statusSurface.Size = new Size(statusGroup.ClientSize.Width - 6, statusGroup.ClientSize.Height - 18);
            statusSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statusSurface.BackColor = Color.FromArgb(192, 192, 192);
            statusGroup.Controls.Add(statusSurface);

            statusFrontPanel = new Panel();
            statusFrontPanel.Dock = DockStyle.Fill;
            statusFrontPanel.BackColor = Color.FromArgb(192, 192, 192);
            statusSurface.Controls.Add(statusFrontPanel);

            flowerPanel = new PixelGerberaPanel();
            flowerPanel.Dock = DockStyle.Fill;
            flowerPanel.Visible = false;
            statusSurface.Controls.Add(flowerPanel);

            statusLamp = new Panel();
            statusLamp.Location = new Point(15, 3);
            statusLamp.Size = new Size(16, 16);
            statusLamp.BorderStyle = BorderStyle.Fixed3D;
            statusFrontPanel.Controls.Add(statusLamp);

            AddValueRow(statusFrontPanel, "Mode:", 27, out statusValue);
            AddValueRow(statusFrontPanel, "Detected game:", 47, out gameValue);
            AddValueRow(statusFrontPanel, "Power plan:", 67, out planValue);
            AddValueRow(statusFrontPanel, "Brightness:", 87, out brightnessValue);
            AddValueRow(statusFrontPanel, "Timer phase:", 107, out timerValue);
            AddValueRow(statusFrontPanel, "Minutes left:", 127, out minutesLeftValue);
            AddValueRow(statusFrontPanel, "Cycle:", 147, out cycleValue);
            AddValueRow(statusFrontPanel, "CPU temp / peak:", 167, out cpuTemperatureValue);
            AddValueRow(statusFrontPanel, "GPU temp / peak:", 187, out gpuTemperatureValue);
            AddValueRow(statusFrontPanel, "Watcher:", 207, out watcherValue);

            flipStatusButton = RetroButton("Flowers", 64, 22);
            flipStatusButton.Location = new Point(statusFrontPanel.ClientSize.Width - 68, 2);
            flipStatusButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            flipStatusButton.Click += delegate { StartStatusPanelFlip(); };
            statusFrontPanel.Controls.Add(flipStatusButton);
            flipStatusButton.BringToFront();

            flowerFlipButton = RetroButton("Status", 64, 22);
            flowerFlipButton.Location = new Point(flowerPanel.ClientSize.Width - 68, 2);
            flowerFlipButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            flowerFlipButton.Click += delegate { StartStatusPanelFlip(); };
            flowerPanel.Controls.Add(flowerFlipButton);
            flowerFlipButton.BringToFront();

            panelFlipTimer.Interval = 15;
            panelFlipTimer.Tick += delegate { AnimateStatusPanelFlip(); };

            enableButton = RetroButton("Enable", 92, 26);
            enableButton.Location = new Point(418, 93);
            enableButton.Click += delegate { EnableGameManagement(); };
            gameSurface.Controls.Add(enableButton);

            pauseButton = RetroButton("Pause", 92, 26);
            pauseButton.Location = new Point(520, 93);
            pauseButton.Click += delegate { PauseGameManagement(); };
            gameSurface.Controls.Add(pauseButton);

            Button refreshButton = RetroButton("Refresh", 92, 26);
            refreshButton.Location = new Point(622, 93);
            refreshButton.Click += delegate { SetFooter("Refreshing status..."); RefreshStatus(); };
            gameSurface.Controls.Add(refreshButton);

            GroupBox settingsGroup = RetroGroup("Settings", 414, 132, 370, 184);
            gameSurface.Controls.Add(settingsGroup);
            Label brightnessLabel = MakeLabel("Gaming brightness:", 16, 28);
            settingsGroup.Controls.Add(brightnessLabel);
            brightnessInput = new NumericUpDown();
            brightnessInput.Location = new Point(150, 25);
            brightnessInput.Size = new Size(72, 21);
            brightnessInput.Minimum = 0;
            brightnessInput.Maximum = 100;
            brightnessInput.TextAlign = HorizontalAlignment.Right;
            settingsGroup.Controls.Add(brightnessInput);
            settingsGroup.Controls.Add(MakeLabel("%", 228, 29));

            settingsGroup.Controls.Add(MakeLabel("Scan interval:", 16, 57));
            pollInput = new NumericUpDown();
            pollInput.Location = new Point(150, 54);
            pollInput.Size = new Size(72, 21);
            pollInput.Minimum = 2;
            pollInput.Maximum = 60;
            pollInput.TextAlign = HorizontalAlignment.Right;
            settingsGroup.Controls.Add(pollInput);
            settingsGroup.Controls.Add(MakeLabel("seconds", 228, 58));

            timerEnabledCheck = new CheckBox();
            timerEnabledCheck.Text = "Game timer:";
            timerEnabledCheck.Location = new Point(16, 84);
            timerEnabledCheck.AutoSize = true;
            timerEnabledCheck.CheckedChanged += delegate
            {
                timerMinutesInput.Enabled = timerEnabledCheck.Checked;
                if (breakMinutesInput != null) breakMinutesInput.Enabled = timerEnabledCheck.Checked;
                UpdateTimerConfirmState();
            };
            settingsGroup.Controls.Add(timerEnabledCheck);

            timerMinutesInput = new NumericUpDown();
            timerMinutesInput.Location = new Point(150, 81);
            timerMinutesInput.Size = new Size(72, 21);
            timerMinutesInput.Minimum = 1;
            timerMinutesInput.Maximum = 240;
            timerMinutesInput.TextAlign = HorizontalAlignment.Right;
            timerMinutesInput.ValueChanged += delegate { UpdateTimerConfirmState(); };
            settingsGroup.Controls.Add(timerMinutesInput);
            settingsGroup.Controls.Add(MakeLabel("minutes", 228, 85));

            settingsGroup.Controls.Add(MakeLabel("Break timer:", 16, 109));
            breakMinutesInput = new NumericUpDown();
            breakMinutesInput.Location = new Point(150, 105);
            breakMinutesInput.Size = new Size(72, 21);
            breakMinutesInput.Minimum = 1;
            breakMinutesInput.Maximum = 60;
            breakMinutesInput.TextAlign = HorizontalAlignment.Right;
            breakMinutesInput.ValueChanged += delegate { UpdateTimerConfirmState(); };
            settingsGroup.Controls.Add(breakMinutesInput);
            settingsGroup.Controls.Add(MakeLabel("minutes", 228, 109));

            confirmTimerButton = RetroButton("Confirm", 68, 25);
            confirmTimerButton.Location = new Point(settingsGroup.ClientSize.Width - 82, 92);
            confirmTimerButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            confirmTimerButton.Click += delegate { ConfirmTimerSettings(); };
            settingsGroup.Controls.Add(confirmTimerButton);

            startupCheck = new CheckBox();
            startupCheck.Text = "Start Game Management when I sign in";
            startupCheck.Location = new Point(16, 130);
            startupCheck.AutoSize = true;
            startupCheck.CheckedChanged += delegate
            {
                if (startupCheck.Focused) SetStartup(startupCheck.Checked);
            };
            settingsGroup.Controls.Add(startupCheck);

            pauseWithEscapeCheck = new CheckBox();
            pauseWithEscapeCheck.Text = "Press Escape at break start/end";
            pauseWithEscapeCheck.Location = new Point(16, 153);
            pauseWithEscapeCheck.AutoSize = true;
            settingsGroup.Controls.Add(pauseWithEscapeCheck);

            GroupBox foldersGroup = RetroGroup("Detected game apps and library folders", 20, 330, 764, 142);
            foldersGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            gameSurface.Controls.Add(foldersGroup);
            Button gameReportButton = RetroButton("Weekly report", 108, 25);
            gameReportButton.Location = new Point(676, 43);
            gameReportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            gameReportButton.Click += delegate { using (WeeklyReportForm report = new WeeklyReportForm(historyPath)) report.ShowDialog(this); };
            gameSurface.Controls.Add(gameReportButton);
            foldersList = new ListBox();
            foldersList.Location = new Point(13, 23);
            foldersList.Size = new Size(628, 95);
            foldersList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            foldersList.HorizontalScrollbar = true;
            foldersGroup.Controls.Add(foldersList);

            Button installedGame = RetroButton("Installed apps...", 108, 25);
            installedGame.Location = new Point(534, 23);
            installedGame.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            installedGame.Click += delegate { ChooseInstalledGameApplication(); };
            foldersGroup.Controls.Add(installedGame);

            Button browseGame = RetroButton("Browse file...", 108, 25);
            browseGame.Location = new Point(654, 23);
            browseGame.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            browseGame.Click += delegate { BrowseGameApplication(); };
            foldersGroup.Controls.Add(browseGame);

            Button addFolder = RetroButton("Add folder...", 108, 25);
            addFolder.Location = new Point(534, 56);
            addFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addFolder.Click += delegate { AddFolder(); };
            foldersGroup.Controls.Add(addFolder);

            Button removeFolder = RetroButton("Remove", 108, 25);
            removeFolder.Location = new Point(654, 56);
            removeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            removeFolder.Click += delegate { RemoveFolder(); };
            foldersGroup.Controls.Add(removeFolder);

            Button saveButton = RetroButton("Save settings", 228, 25);
            saveButton.Location = new Point(534, 89);
            saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveButton.Click += delegate { SaveSettingsAsync(null, null); };
            foldersGroup.Controls.Add(saveButton);

            logGroup = RetroGroup("Recent activity", 20, 484, 764, 95);
            logGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            logGroup.Visible = false;
            gameSurface.Controls.Add(logGroup);
            logBox = new TextBox();
            logBox.Location = new Point(13, 22);
            logBox.Size = new Size(631, 73);
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.BackColor = Color.White;
            logBox.Font = new Font("Lucida Console", 8F);
            logGroup.Controls.Add(logBox);

            Button openLog = RetroButton("Open log", 92, 25);
            openLog.Location = new Point(654, 22);
            openLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openLog.Click += delegate { OpenLog(); };
            logGroup.Controls.Add(openLog);

            Button diagnostics = RetroButton("Diagnostics", 92, 25);
            diagnostics.Location = new Point(654, 55);
            diagnostics.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            diagnostics.Click += delegate { OpenDiagnostics(); };
            logGroup.Controls.Add(diagnostics);

            trayCheck = new CheckBox();
            trayCheck.Text = "Keep running in notification area";
            trayCheck.Checked = true;
            trayCheck.AutoSize = true;
            trayCheck.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            trayCheck.Location = new Point(22, 558);
            gameSurface.Controls.Add(trayCheck);

            toggleLogButton = RetroButton("Show activity", 92, 25);
            toggleLogButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            toggleLogButton.Click += delegate { ToggleActivity(); };
            gameSurface.Controls.Add(toggleLogButton);

            footerLabel = new Label();
            footerLabel.Text = "Ready.";
            footerLabel.BorderStyle = BorderStyle.Fixed3D;
            footerLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            footerLabel.Location = new Point(20, 582);
            footerLabel.Size = new Size(764, 20);
            footerLabel.TextAlign = ContentAlignment.MiddleLeft;
            gameSurface.Controls.Add(footerLabel);

            BuildWorkSurface();

            modeFlipButton = RetroButton("Work mode", 108, 25);
            modeFlipButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            modeFlipButton.Click += delegate { FlipManagementMode(); };
            gameSurface.Controls.Add(modeFlipButton);
            modeFlipButton.BringToFront();

            workspaceFlipTimer.Interval = 15;
            workspaceFlipTimer.Tick += delegate { AnimateManagementModeFlip(); };

            body.Resize += delegate
            {
                int contentMargin = 20;
                int sectionGap = 18;
                int contentWidth = Math.Max(640, body.ClientSize.Width - (contentMargin * 2));
                int leftSectionWidth = (contentWidth - sectionGap) / 2;
                int rightSectionLeft = contentMargin + leftSectionWidth + sectionGap;
                int rightSectionWidth = contentWidth - leftSectionWidth - sectionGap;

                statusGroup.Width = leftSectionWidth;
                settingsGroup.Left = rightSectionLeft;
                settingsGroup.Width = rightSectionWidth;

                int topButtonGap = 10;
                int topButtonWidth = Math.Max(72, (rightSectionWidth - (topButtonGap * 2)) / 3);
                enableButton.Left = rightSectionLeft;
                enableButton.Width = topButtonWidth;
                pauseButton.Left = enableButton.Right + topButtonGap;
                pauseButton.Width = topButtonWidth;
                refreshButton.Left = pauseButton.Right + topButtonGap;
                refreshButton.Width = rightSectionLeft + rightSectionWidth - refreshButton.Left;

                foldersGroup.Width = contentWidth;
                logGroup.Width = contentWidth;

                int folderPadding = 13;
                int folderGap = 10;
                int folderActionWidth = Math.Max(92, Math.Min(120, foldersGroup.ClientSize.Width / 7));
                int folderButtonGap = 8;
                int folderActionLeft = foldersGroup.ClientSize.Width - folderPadding - (folderActionWidth * 2) - folderButtonGap;
                foldersList.Width = Math.Max(220, folderActionLeft - foldersList.Left - folderGap);
                installedGame.Left = folderActionLeft;
                installedGame.Width = folderActionWidth;
                browseGame.Left = installedGame.Right + folderButtonGap;
                browseGame.Width = folderActionWidth;
                addFolder.Left = folderActionLeft;
                addFolder.Width = folderActionWidth;
                removeFolder.Left = addFolder.Right + folderButtonGap;
                removeFolder.Width = folderActionWidth;
                saveButton.Left = folderActionLeft;
                saveButton.Width = (folderActionWidth * 2) + folderButtonGap;

                footerLabel.Top = body.ClientSize.Height - footerLabel.Height - 7;
                footerLabel.Width = contentWidth;
                trayCheck.Top = footerLabel.Top - trayCheck.Height - 4;
                toggleLogButton.Top = trayCheck.Top - 4;

                int logPadding = 13;
                int logGap = 10;
                int actionWidth = Math.Max(92, Math.Min(130, logGroup.ClientSize.Width / 7));
                int actionLeft = logGroup.ClientSize.Width - logPadding - actionWidth;
                toggleLogButton.Left = logGroup.Left + actionLeft;
                toggleLogButton.Width = actionWidth;
                toggleLogButton.Height = 25;
                modeFlipButton.Left = toggleLogButton.Left - modeFlipButton.Width - 10;
                modeFlipButton.Top = toggleLogButton.Top;
                logBox.Width = Math.Max(220, actionLeft - logBox.Left - logGap);
                openLog.Left = actionLeft;
                openLog.Width = actionWidth;
                openLog.Height = 25;
                diagnostics.Left = actionLeft;
                diagnostics.Width = actionWidth;
                diagnostics.Height = 25;
                diagnostics.Top = openLog.Top + 33;
                logBox.Top = openLog.Top;
                logBox.Height = diagnostics.Bottom - logBox.Top;
            };

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                StartUiSignalListener();
                RefreshStatus();
                if (enableOnStart) EnableGameManagement(false);
                if (showForBreakOnStart) ShowForBreak();
                else if (hideOnStart) BeginInvoke(new MethodInvoker(HideAfterBreak));
                if (Visible || File.Exists(statePath)) RefreshTemperatureMetrics();
            };
        }

        private void BuildWorkSurface()
        {
            Label brand = new Label();
            brand.Text = "WORK MANAGEMENT";
            brand.Font = new Font("Arial", 26F, FontStyle.Bold);
            brand.AutoSize = true;
            brand.Location = new Point(17, 12);
            workSurface.Controls.Add(brand);

            Label subtitle = new Label();
            subtitle.Text = "Selected-app focus sessions and break controller";
            subtitle.AutoSize = true;
            subtitle.Location = new Point(21, 53);
            workSurface.Controls.Add(subtitle);

            GroupBox status = RetroGroup("Current work", 20, 82, 764, 148);
            status.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            workSurface.Controls.Add(status);
            Panel statusPanel = new Panel();
            statusPanel.Location = new Point(3, 15);
            statusPanel.Size = new Size(status.ClientSize.Width - 6, status.ClientSize.Height - 18);
            statusPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statusPanel.BackColor = Color.FromArgb(192, 192, 192);
            status.Controls.Add(statusPanel);
            AddValueRow(statusPanel, "Mode:", 12, out workModeValue);
            AddValueRow(statusPanel, "Detected apps:", 34, out workAppsValue);
            AddValueRow(statusPanel, "Session time:", 56, out workElapsedValue);
            AddValueRow(statusPanel, "CPU temp:", 78, out workCpuValue);
            AddValueRow(statusPanel, "GPU temp:", 100, out workGpuValue);
            AddWorkTimerRow(statusPanel, "Timer phase:", 12, out workTimerPhaseValue);
            AddWorkTimerRow(statusPanel, "Minutes left:", 34, out workMinutesLeftValue);
            AddWorkTimerRow(statusPanel, "Cycle:", 56, out workCycleValue);

            GroupBox apps = RetroGroup("Selected work applications", 20, 244, 764, 222);
            apps.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            workSurface.Controls.Add(apps);
            workAppsList = new ListBox();
            workAppsList.Location = new Point(13, 23);
            workAppsList.Size = new Size(628, 154);
            workAppsList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            workAppsList.HorizontalScrollbar = true;
            apps.Controls.Add(workAppsList);

            Button add = RetroButton("Browse file...", 92, 25);
            add.Location = new Point(654, 23);
            add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            add.Click += delegate { AddWorkApplication(); };
            apps.Controls.Add(add);

            Button installed = RetroButton("Installed apps...", 92, 25);
            installed.Location = new Point(654, 56);
            installed.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            installed.Click += delegate { ChooseInstalledWorkApplication(); };
            apps.Controls.Add(installed);

            Button remove = RetroButton("Remove", 92, 25);
            remove.Location = new Point(654, 89);
            remove.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            remove.Click += delegate { RemoveWorkApplication(); };
            apps.Controls.Add(remove);

            Button save = RetroButton("Save apps", 92, 25);
            save.Location = new Point(654, 122);
            save.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            save.Click += delegate { SaveWorkApplications(); };
            apps.Controls.Add(save);

            Button launch = RetroButton("Launch selected", 92, 25);
            launch.Location = new Point(654, 155);
            launch.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            launch.Click += delegate { LaunchSelectedWorkApplication(); };
            apps.Controls.Add(launch);

            Label note = MakeLabel("Only the exact .exe files selected here start Work Management. Gaming power and brightness settings are not used.", 13, 190);
            note.AutoSize = false;
            note.Size = new Size(733, 18);
            note.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            apps.Controls.Add(note);

            workFooterLabel = new Label();
            workFooterLabel.Text = "Choose Installed apps... or Browse file... to select a work application.";
            workFooterLabel.BorderStyle = BorderStyle.Fixed3D;
            workFooterLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            workFooterLabel.Location = new Point(20, 515);
            workFooterLabel.Size = new Size(764, 20);
            workFooterLabel.TextAlign = ContentAlignment.MiddleLeft;
            workSurface.Controls.Add(workFooterLabel);

            workModeFlipButton = RetroButton("Game mode", 108, 25);
            workModeFlipButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            workModeFlipButton.Click += delegate { FlipManagementMode(); };
            workSurface.Controls.Add(workModeFlipButton);
            workModeFlipButton.BringToFront();

            Button weeklyReportButton = RetroButton("Weekly report", 108, 25);
            weeklyReportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            weeklyReportButton.Click += delegate { using (WeeklyReportForm report = new WeeklyReportForm(historyPath)) report.ShowDialog(this); };
            workSurface.Controls.Add(weeklyReportButton);

            GroupBox workTimerSettings = RetroGroup("Work timer", 20, 468, 764, 43);
            workTimerSettings.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            workSurface.Controls.Add(workTimerSettings);
            workTimerEnabledCheck = new CheckBox { Text = "On", Location = new Point(13, 18), AutoSize = true };
            workTimerEnabledCheck.CheckedChanged += delegate
            {
                if (workTimerMinutesInput != null) workTimerMinutesInput.Enabled = workTimerEnabledCheck.Checked;
                if (workBreakMinutesInput != null) workBreakMinutesInput.Enabled = workTimerEnabledCheck.Checked;
            };
            workTimerSettings.Controls.Add(workTimerEnabledCheck);
            workTimerSettings.Controls.Add(MakeLabel("Work:", 75, 19));
            workTimerMinutesInput = new NumericUpDown { Location = new Point(115, 16), Size = new Size(55, 21), Minimum = 1, Maximum = 240, TextAlign = HorizontalAlignment.Right };
            workTimerSettings.Controls.Add(workTimerMinutesInput);
            workTimerSettings.Controls.Add(MakeLabel("min", 175, 19));
            workTimerSettings.Controls.Add(MakeLabel("Break:", 215, 19));
            workBreakMinutesInput = new NumericUpDown { Location = new Point(260, 16), Size = new Size(55, 21), Minimum = 1, Maximum = 60, TextAlign = HorizontalAlignment.Right };
            workTimerSettings.Controls.Add(workBreakMinutesInput);
            workTimerSettings.Controls.Add(MakeLabel("min", 320, 19));
            Button saveTimer = RetroButton("Save timer", 92, 25);
            saveTimer.Location = new Point(654, 14);
            saveTimer.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveTimer.Click += delegate
            {
                SaveSettingsAsync(null, delegate(bool saved)
                {
                    if (saved) workFooterLabel.Text = "Work timer saved.";
                });
            };
            workTimerSettings.Controls.Add(saveTimer);

            workSurface.Resize += delegate
            {
                int contentMargin = 20;
                int contentWidth = Math.Max(640, workSurface.ClientSize.Width - (contentMargin * 2));
                status.Width = contentWidth;
                apps.Width = contentWidth;
                int padding = 13;
                int gap = 10;
                int actionWidth = Math.Max(92, Math.Min(130, apps.ClientSize.Width / 7));
                int actionLeft = apps.ClientSize.Width - padding - actionWidth;
                workAppsList.Width = Math.Max(220, actionLeft - workAppsList.Left - gap);
                add.Left = actionLeft;
                add.Width = actionWidth;
                remove.Left = actionLeft;
                remove.Width = actionWidth;
                save.Left = actionLeft;
                save.Width = actionWidth;
                installed.Left = actionLeft;
                installed.Width = actionWidth;
                launch.Left = actionLeft;
                launch.Width = actionWidth;
                note.Width = apps.ClientSize.Width - (padding * 2);
                workTimerSettings.Width = contentWidth;
                saveTimer.Left = workTimerSettings.ClientSize.Width - padding - actionWidth;
                saveTimer.Width = actionWidth;
                workFooterLabel.Top = workSurface.ClientSize.Height - workFooterLabel.Height - 7;
                workFooterLabel.Width = contentWidth;
                workModeFlipButton.Left = workSurface.ClientSize.Width - contentMargin - workModeFlipButton.Width;
                workModeFlipButton.Top = 43;
                weeklyReportButton.Left = workModeFlipButton.Left - weeklyReportButton.Width - 10;
                weeklyReportButton.Top = workModeFlipButton.Top;
            };
        }

        private void AddWorkTimerRow(Control parent, string caption, int y, out Label value)
        {
            Label name = MakeLabel(caption, 390, y);
            name.Size = new Size(100, 16);
            parent.Controls.Add(name);
            value = MakeLabel("Ready", 500, y);
            value.Font = new Font(Font, FontStyle.Bold);
            parent.Controls.Add(value);
        }

        private void AddWorkApplication()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose a work application";
                dialog.Filter = "Applications (*.exe)|*.exe";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string path in dialog.FileNames)
                {
                    bool exists = workAppsList.Items.Cast<object>().Any(item => string.Equals(item.ToString(), path, StringComparison.OrdinalIgnoreCase));
                    if (!exists) workAppsList.Items.Add(path);
                }
                workFooterLabel.Text = "Selection changed. Choose Save apps to apply it.";
            }
        }

        private void ChooseInstalledWorkApplication()
        {
            using (InstalledAppsForm picker = new InstalledAppsForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedPath)) return;
                if (!workAppsList.Items.Cast<object>().Any(item => string.Equals(item.ToString(), picker.SelectedPath, StringComparison.OrdinalIgnoreCase)))
                    workAppsList.Items.Add(picker.SelectedPath);
                workFooterLabel.Text = "Selection changed. Choose Save apps to apply it.";
            }
        }

        private void LaunchSelectedWorkApplication()
        {
            string path = Convert.ToString(workAppsList.SelectedItem);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                RetroMessage("Select an available application first. Browse file... can add portable apps.", "Game Management");
                return;
            }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) }); }
            catch (Exception ex) { ShowError("The application could not be launched.\r\n\r\n" + ex.Message); }
        }

        private void RemoveWorkApplication()
        {
            while (workAppsList.SelectedIndices.Count > 0)
                workAppsList.Items.RemoveAt(workAppsList.SelectedIndices[0]);
            workFooterLabel.Text = "Selection changed. Choose Save apps to apply it.";
        }

        private void SaveWorkApplications()
        {
            SaveSettingsAsync(null, delegate(bool saved)
            {
                if (saved) workFooterLabel.Text = "Work applications saved. Monitoring restarted if needed.";
            });
        }

        private void FlipManagementMode()
        {
            if (workspaceFlipTimer.Enabled || modeSwitchInProgress) return;
            targetWorkView = !workView;
            modeSwitchInProgress = true;
            System.Threading.Interlocked.Increment(ref statusRefreshGeneration);
            System.Threading.Interlocked.Exchange(ref statusRefreshPending, 1);
            SaveSettingsAsync(targetWorkView ? "work" : "game", delegate(bool saved)
            {
                if (!saved)
                {
                    modeSwitchInProgress = false;
                    return;
                }
                BeginManagementModeFlip();
            });
        }

        private void BeginManagementModeFlip()
        {
            workspaceFlipFrame = 0;
            workspaceFlipSurface = workView ? workSurface : gameSurface;
            workspaceFlipBounds = workspaceFlipSurface.Bounds;
            workspaceFlipSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            modeFlipButton.Enabled = false;
            workModeFlipButton.Enabled = false;
            workspaceFlipTimer.Start();
        }

        private void AnimateManagementModeFlip()
        {
            const int totalFrames = 18;
            const int half = totalFrames / 2;
            workspaceFlipFrame++;
            if (workspaceFlipFrame <= half)
            {
                double progress = workspaceFlipFrame / (double)half;
                int width = Math.Max(2, (int)Math.Round(workspaceFlipBounds.Width * Math.Cos(progress * Math.PI / 2.0)));
                workspaceFlipSurface.SetBounds(workspaceFlipBounds.Left + (workspaceFlipBounds.Width - width) / 2, workspaceFlipBounds.Top, width, workspaceFlipBounds.Height);
                if (workspaceFlipFrame < half) return;
                workspaceFlipSurface.Visible = false;
                workView = targetWorkView;
                workspaceFlipSurface = workView ? workSurface : gameSurface;
                workspaceFlipSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
                workspaceFlipSurface.SetBounds(workspaceFlipBounds.Left + workspaceFlipBounds.Width / 2, workspaceFlipBounds.Top, 2, workspaceFlipBounds.Height);
                workspaceFlipSurface.Visible = true;
                workspaceFlipSurface.BringToFront();
                return;
            }

            double expansion = (workspaceFlipFrame - half) / (double)half;
            int expandedWidth = Math.Max(2, (int)Math.Round(workspaceFlipBounds.Width * Math.Sin(expansion * Math.PI / 2.0)));
            workspaceFlipSurface.SetBounds(workspaceFlipBounds.Left + (workspaceFlipBounds.Width - expandedWidth) / 2, workspaceFlipBounds.Top, expandedWidth, workspaceFlipBounds.Height);
            if (workspaceFlipFrame < totalFrames) return;
            workspaceFlipTimer.Stop();
            workspaceFlipSurface.Bounds = workspaceFlipBounds;
            workspaceFlipSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            modeFlipButton.Enabled = true;
            workModeFlipButton.Enabled = true;
            modeSwitchInProgress = false;
            RefreshStatus();
        }

        private void ShowManagementMode(bool showWork)
        {
            workView = showWork;
            gameSurface.Visible = !showWork;
            workSurface.Visible = showWork;
            if (showWork) workSurface.BringToFront(); else gameSurface.BringToFront();
        }

        private string[] GetRunningSelectedWorkApps(string[] selectedPaths)
        {
            if (selectedPaths == null || selectedPaths.Length == 0) return new string[0];
            HashSet<string> selected = new HashSet<string>(selectedPaths.Where(File.Exists), StringComparer.OrdinalIgnoreCase);
            HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    string path = process.MainModule.FileName;
                    if (selected.Contains(path)) running.Add(Path.GetFileNameWithoutExtension(path));
                }
                catch { }
                finally { process.Dispose(); }
            }
            return running.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private void RefreshWorkStatus()
        {
            if (!workView || settings == null) return;
            string[] running = GetRunningSelectedWorkApps(settings.workApps == null ? new string[0] : settings.workApps.ToArray());
            DateTime? sessionStartedAt = null;
            try
            {
                if (File.Exists(statePath))
                {
                    Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
                    object mode;
                    object started;
                    if (state.TryGetValue("Mode", out mode) && string.Equals(Convert.ToString(mode), "work", StringComparison.OrdinalIgnoreCase) && state.TryGetValue("ManagementStarted", out started))
                    {
                        DateTime began;
                        if (DateTime.TryParse(Convert.ToString(started), null, System.Globalization.DateTimeStyles.RoundtripKind, out began))
                            sessionStartedAt = began.ToLocalTime();
                    }
                }
            }
            catch { }
            ApplyWorkStatus(running, sessionStartedAt);
        }

        private void ApplyWorkStatus(string[] running, DateTime? sessionStartedAt)
        {
            if (!workView || settings == null) return;
            SetLabelText(workAppsValue, running != null && running.Length > 0 ? string.Join(", ", running) : "None");
            workSessionStartedAt = sessionStartedAt;
            workModeValue.Text = workSessionStartedAt.HasValue ? "WORK ACTIVE" : "Ready / monitoring";
            workModeValue.ForeColor = workSessionStartedAt.HasValue ? Color.Green : Color.Black;
            RefreshWorkElapsedDisplay();
            workCpuValue.Text = cpuTemperatureValue == null ? "Unavailable" : cpuTemperatureValue.Text;
            workCpuValue.ForeColor = cpuTemperatureValue == null ? Color.DimGray : cpuTemperatureValue.ForeColor;
            workGpuValue.Text = gpuTemperatureValue == null ? "Unavailable" : gpuTemperatureValue.Text;
            workGpuValue.ForeColor = gpuTemperatureValue == null ? Color.DimGray : gpuTemperatureValue.ForeColor;
            RefreshWorkTimerDisplay();
        }

        private void RefreshWorkTimerDisplay()
        {
            if (workTimerPhaseValue == null || settings == null) return;
            if (!settings.workTimerEnabled)
            {
                SetLabelText(workTimerPhaseValue, "Off");
                SetLabelText(workMinutesLeftValue, "--:--");
                SetLabelText(workCycleValue, "0");
                return;
            }
            if (!workSessionStartedAt.HasValue || !File.Exists(workTimerStatePath))
            {
                SetLabelText(workTimerPhaseValue, workSessionStartedAt.HasValue ? "Starting" : "Ready");
                SetLabelText(workMinutesLeftValue, settings.workTimerMinutes.ToString("00") + ":00");
                SetLabelText(workCycleValue, workSessionStartedAt.HasValue ? "1" : "0");
                return;
            }
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(workTimerStatePath));
                if (!string.Equals(Convert.ToString(state["Mode"]), "work", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                DateTime deadline = DateTime.Parse(Convert.ToString(state["Deadline"]), null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
                int seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.Now).TotalSeconds));
                SetLabelText(workTimerPhaseValue, Convert.ToString(state["Phase"]));
                SetLabelText(workMinutesLeftValue, string.Format("{0:00}:{1:00}", seconds / 60, seconds % 60));
                SetLabelText(workCycleValue, Convert.ToString(state["Cycle"]));
            }
            catch
            {
                SetLabelText(workTimerPhaseValue, "Unavailable");
                SetLabelText(workMinutesLeftValue, "--:--");
            }
        }

        private void RefreshWorkElapsedDisplay()
        {
            if (workElapsedValue == null) return;
            TimeSpan elapsed = workSessionStartedAt.HasValue
                ? DateTime.Now - workSessionStartedAt.Value
                : TimeSpan.Zero;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            int hours = Math.Max(0, (int)Math.Floor(elapsed.TotalHours));
            SetLabelText(workElapsedValue, string.Format("{0:00}:{1:00}:{2:00}", hours, elapsed.Minutes, elapsed.Seconds));
        }

        private void InitializeUiSignals()
        {
            bool created;
            showMainEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, UiSignals.ShowMainName, out created);
            showBreakEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, UiSignals.ShowBreakName, out created);
            hideBreakEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, UiSignals.HideBreakName, out created);
        }

        private void StartUiSignalListener()
        {
            if (uiSignalThread != null) return;
            uiContext = System.Threading.SynchronizationContext.Current;
            if (uiContext == null) uiContext = new WindowsFormsSynchronizationContext();

            uiSignalThread = new System.Threading.Thread(new System.Threading.ThreadStart(delegate
            {
                System.Threading.WaitHandle[] signals = { showMainEvent, showBreakEvent, hideBreakEvent };
                while (!stopUiSignalThread)
                {
                    int signalIndex;
                    try { signalIndex = System.Threading.WaitHandle.WaitAny(signals, 500); }
                    catch (ObjectDisposedException) { return; }
                    if (stopUiSignalThread) return;
                    if (signalIndex == System.Threading.WaitHandle.WaitTimeout) continue;

                    int requestedAction = signalIndex;
                    try
                    {
                        uiContext.Post(delegate
                        {
                            if (IsDisposed || stopUiSignalThread) return;
                            if (requestedAction == 0) ShowFromTray(false);
                            else if (requestedAction == 1) ShowForBreak();
                            else if (requestedAction == 2) HideAfterBreak();
                        }, null);
                    }
                    catch (System.ComponentModel.InvalidAsynchronousStateException) { return; }
                }
            }));
            uiSignalThread.IsBackground = true;
            uiSignalThread.Name = "Game Management UI signal listener";
            uiSignalThread.Start();
        }

        private void ShowForBreak()
        {
            ShowFromTray(true);
            TopMost = true;
            BringToFront();
            SetFooter("Break started. Break timer is running.");
        }

        private void HideAfterBreak()
        {
            TopMost = false;
            Hide();
            ShowInTaskbar = false;
        }

        private void RefreshTemperatureMetrics()
        {
            string currentSessionKey = GetRuntimeSessionKey();
            if (!string.Equals(currentSessionKey, temperatureSessionKey, StringComparison.Ordinal))
            {
                ResetTemperaturePeaks();
                temperatureSessionKey = currentSessionKey;
            }
            if (!Visible && !File.Exists(statePath))
            {
                if (File.Exists(performanceStatePath)) File.Delete(performanceStatePath);
                return;
            }
            if (System.Threading.Interlocked.Exchange(ref temperatureRefreshRunning, 1) != 0) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                float? cpuTemperature = ReadCpuTemperature();
                float? gpuTemperature = ReadGpuTemperature();
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        ApplyTemperatureMetrics(currentSessionKey, cpuTemperature, gpuTemperature);
                    }));
                }
                catch
                {
                    System.Threading.Interlocked.Exchange(ref temperatureRefreshRunning, 0);
                }
            });
        }

        private void ApplyTemperatureMetrics(string sessionKey, float? cpuTemperature, float? gpuTemperature)
        {
            System.Threading.Interlocked.Exchange(ref temperatureRefreshRunning, 0);
            if (exiting || IsDisposed || !IsHandleCreated) return;
            if (!string.Equals(sessionKey, GetRuntimeSessionKey(), StringComparison.Ordinal))
            {
                RefreshTemperatureMetrics();
                return;
            }
            if (cpuTemperature.HasValue)
                peakCpuTemperature = !peakCpuTemperature.HasValue ? cpuTemperature : Math.Max(peakCpuTemperature.Value, cpuTemperature.Value);
            if (gpuTemperature.HasValue)
                peakGpuTemperature = !peakGpuTemperature.HasValue ? gpuTemperature : Math.Max(peakGpuTemperature.Value, gpuTemperature.Value);

            SetTemperatureLabel(cpuTemperatureValue, cpuTemperature, peakCpuTemperature, true);
            SetTemperatureLabel(gpuTemperatureValue, gpuTemperature, peakGpuTemperature, false);
            WriteTemperatureSnapshot(cpuTemperature, gpuTemperature);
        }

        private string GetRuntimeSessionKey()
        {
            if (!File.Exists(statePath)) return null;
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
                object mode, started;
                if (state.TryGetValue("Mode", out mode) && state.TryGetValue("ManagementStarted", out started))
                    return Convert.ToString(mode) + "|" + Convert.ToString(started);
            }
            catch { }
            return null;
        }

        private float? ReadCpuTemperature()
        {
            float? afterburnerTemperature = ReadAfterburnerCpuTemperature();
            if (afterburnerTemperature.HasValue) return afterburnerTemperature;

            float? lenovoTemperature = ReadLenovoTemperature("GetCPUTemp");
            if (lenovoTemperature.HasValue) return lenovoTemperature;

            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\WMI");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature")))
                {
                    float? hottest = null;
                    foreach (ManagementObject zone in searcher.Get())
                    {
                        float celsius = Convert.ToSingle(zone["CurrentTemperature"], System.Globalization.CultureInfo.InvariantCulture) / 10F - 273.15F;
                        if (celsius >= 10F && celsius <= 125F)
                            hottest = !hottest.HasValue ? celsius : Math.Max(hottest.Value, celsius);
                    }
                    return hottest;
                }
            }
            catch { return null; }
        }

        private float? ReadAfterburnerCpuTemperature()
        {
            const uint MahMatureSignature = 0x4D41484D;
            const uint CpuTemperatureSourceId = 0x00000080;
            const long MinimumHeaderSize = 32;
            const long MinimumEntrySize = 1324;
            const long DataOffset = 1300;
            const long SourceIdOffset = 1320;

            try
            {
                using (MemoryMappedFile map = MemoryMappedFile.OpenExisting("MAHMSharedMemory", MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    uint signature = view.ReadUInt32(0);
                    uint version = view.ReadUInt32(4);
                    uint headerSize = view.ReadUInt32(8);
                    uint entryCount = view.ReadUInt32(12);
                    uint entrySize = view.ReadUInt32(16);

                    if (signature != MahMatureSignature || version < 0x00020000) return null;
                    if (headerSize < MinimumHeaderSize || entrySize < MinimumEntrySize || entryCount == 0 || entryCount > 512) return null;

                    long capacity = view.Capacity;
                    long requiredSize = (long)headerSize + (long)entryCount * entrySize;
                    if (requiredSize <= 0 || requiredSize > capacity) return null;

                    for (uint index = 0; index < entryCount; index++)
                    {
                        long entryOffset = (long)headerSize + (long)index * entrySize;
                        if (view.ReadUInt32(entryOffset + SourceIdOffset) != CpuTemperatureSourceId) continue;
                        float temperature = view.ReadSingle(entryOffset + DataOffset);
                        if (!float.IsNaN(temperature) && !float.IsInfinity(temperature) && temperature >= 10F && temperature <= 125F)
                            return temperature;
                    }
                }
            }
            catch (FileNotFoundException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (ArgumentException) { }
            return null;
        }

        private float? ReadGpuTemperature()
        {
            try
            {
                string output = RunCapture("nvidia-smi.exe", "--query-gpu=temperature.gpu --format=csv,noheader,nounits");
                string firstLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                float value;
                if (!string.IsNullOrWhiteSpace(firstLine) && float.TryParse(firstLine.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value >= 10F && value <= 125F)
                    return value;
            }
            catch { }
            return ReadLenovoTemperature("GetGPUTemp");
        }

        private float? ReadLenovoTemperature(string methodName)
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\WMI");
                ManagementPath path = new ManagementPath("LENOVO_GAMEZONE_DATA");
                using (ManagementClass sensor = new ManagementClass(scope, path, null))
                using (ManagementBaseObject result = sensor.InvokeMethod(methodName, null, null))
                {
                    if (result == null || result["Data"] == null) return null;
                    float value = Convert.ToSingle(result["Data"], System.Globalization.CultureInfo.InvariantCulture);
                    return value >= 10F && value <= 125F ? (float?)value : null;
                }
            }
            catch { return null; }
        }

        private void WriteTemperatureSnapshot(float? cpuTemperature, float? gpuTemperature)
        {
            try
            {
                if (!File.Exists(statePath))
                {
                    if (File.Exists(performanceStatePath)) File.Delete(performanceStatePath);
                    return;
                }
                Dictionary<string, object> snapshot = new Dictionary<string, object>();
                snapshot["CurrentCpuC"] = cpuTemperature.HasValue ? (object)Math.Round(cpuTemperature.Value, 1) : null;
                snapshot["PeakCpuC"] = peakCpuTemperature.HasValue ? (object)Math.Round(peakCpuTemperature.Value, 1) : null;
                snapshot["CurrentGpuC"] = gpuTemperature.HasValue ? (object)Math.Round(gpuTemperature.Value, 1) : null;
                snapshot["PeakGpuC"] = peakGpuTemperature.HasValue ? (object)Math.Round(peakGpuTemperature.Value, 1) : null;
                snapshot["UpdatedAt"] = DateTime.Now.ToString("o");
                File.WriteAllText(performanceStatePath, PrettyJson(json.Serialize(snapshot)), new UTF8Encoding(false));
            }
            catch { }
        }

        private static void SetTemperatureLabel(Label label, float? current, float? peak, bool cpu)
        {
            if (label == null) return;
            if (!current.HasValue)
            {
                SetLabelText(label, "Unavailable");
                label.ForeColor = Color.DimGray;
                return;
            }

            float warmAt = cpu ? 80F : 75F;
            float dangerAt = cpu ? 90F : 87F;
            string condition;
            if (current.Value >= dangerAt)
            {
                condition = "Danger";
                label.ForeColor = Color.Red;
            }
            else if (current.Value >= warmAt)
            {
                condition = "Warm";
                label.ForeColor = Color.DarkOrange;
            }
            else
            {
                condition = "Safe";
                label.ForeColor = Color.Green;
            }
            string peakText = peak.HasValue ? Math.Round(peak.Value).ToString("0") : "--";
            SetLabelText(label, Math.Round(current.Value).ToString("0") + "°C | " + peakText + "°C  " + condition);
        }

        private void ResetTemperaturePeaks()
        {
            peakCpuTemperature = null;
            peakGpuTemperature = null;
        }

        private void StartStatusPanelFlip()
        {
            if (panelFlipping) return;
            panelFlipping = true;
            panelFlipFrame = 0;
            panelFlipBounds = statusSurface.Bounds;
            statusSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;
            flipStatusButton.Enabled = false;
            flowerFlipButton.Enabled = false;
            panelFlipTimer.Start();
        }

        private void AnimateStatusPanelFlip()
        {
            const int totalFrames = 18;
            panelFlipFrame++;
            double progress = Math.Min(1.0, panelFlipFrame / (double)totalFrames);
            int width = Math.Max(2, (int)Math.Round(panelFlipBounds.Width * Math.Abs(Math.Cos(progress * Math.PI))));
            statusSurface.Left = panelFlipBounds.Left + (panelFlipBounds.Width - width) / 2;
            statusSurface.Width = width;

            if (panelFlipFrame == totalFrames / 2)
                ShowFlowerPanel(!showingFlowers);

            if (panelFlipFrame < totalFrames) return;
            panelFlipTimer.Stop();
            statusSurface.Bounds = panelFlipBounds;
            statusSurface.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            flipStatusButton.Enabled = true;
            flowerFlipButton.Enabled = true;
            panelFlipping = false;
        }

        private void ShowFlowerPanel(bool showFlowers)
        {
            showingFlowers = showFlowers;
            statusFrontPanel.Visible = !showFlowers;
            flowerPanel.Visible = showFlowers;
            if (showFlowers) flowerPanel.BringToFront(); else statusFrontPanel.BringToFront();
            statusGroup.Text = showFlowers ? "Pixel gerberas" : "Current status";
            if (showFlowers) flowerFlipButton.BringToFront(); else flipStatusButton.BringToFront();
        }

        private void ToggleActivity()
        {
            if (activityVisible)
            {
                expandedClientHeight = Math.Max(CompactExpandedHeight, ClientSize.Height);
                activityVisible = false;
                logGroup.Visible = false;
                toggleLogButton.Text = "Show activity";
                MinimumSize = new Size(720, 570);
                ClientSize = new Size(ClientSize.Width, 570);
                SetFooter("Recent activity hidden.");
            }
            else
            {
                activityVisible = true;
                logGroup.Visible = true;
                toggleLogButton.Text = "Hide activity";
                MinimumSize = new Size(720, CompactExpandedHeight);
                ClientSize = new Size(ClientSize.Width, Math.Max(CompactExpandedHeight, expandedClientHeight));
                SetFooter("Recent activity shown.");
                System.Threading.Interlocked.Increment(ref statusRefreshGeneration);
                RefreshStatus();
            }
        }

        internal void SetActivityVisibleForPreview(bool visible)
        {
            if (activityVisible != visible) ToggleActivity();
        }

        internal void SetFlowerPanelForPreview(bool visible)
        {
            ShowFlowerPanel(visible);
        }

        internal void SetWorkViewForPreview(bool visible)
        {
            ShowManagementMode(visible);
            if (visible) RefreshWorkStatus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Raised);
        }

        private void BuildTrayIcon()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Show Game Management", null, delegate { ShowFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Enable", null, delegate { EnableGameManagement(); });
            menu.Items.Add("Pause", null, delegate { PauseGameManagement(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit application", null, delegate { ExitApplication(); });
            trayIcon.Icon = LoadApplicationIcon();
            trayIcon.Text = "Game Management";
            trayIcon.ContextMenuStrip = menu;
            trayIcon.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ShowFromTray();
            };
            trayIcon.DoubleClick += delegate { ShowFromTray(); };
            trayIcon.Visible = true;
        }

        private Icon LoadApplicationIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Application;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }

        private GroupBox RetroGroup(string text, int x, int y, int width, int height)
        {
            GroupBox box = new GroupBox();
            box.Text = text;
            box.Location = new Point(x, y);
            box.Size = new Size(width, height);
            return box;
        }

        private Button RetroButton(string text, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.FromArgb(192, 192, 192);
            return button;
        }

        private Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            return label;
        }

        private void AddValueRow(Control parent, string labelText, int y, out Label valueLabel)
        {
            Label name = MakeLabel(labelText, 18, y);
            name.Size = new Size(108, 16);
            parent.Controls.Add(name);
            valueLabel = MakeLabel("Checking...", 132, y);
            valueLabel.Font = new Font(Font, FontStyle.Bold);
            valueLabel.MaximumSize = new Size(220, 18);
            parent.Controls.Add(valueLabel);
        }

        private void LoadSettings()
        {
            try
            {
                string settingsText = File.ReadAllText(settingsPath);
                settings = json.Deserialize<GameSettings>(settingsText);
                Dictionary<string, object> settingsMap = json.Deserialize<Dictionary<string, object>>(settingsText);
                if (!settingsMap.ContainsKey("gameTimerEnabled")) settings.gameTimerEnabled = true;
                if (!settingsMap.ContainsKey("pauseGameWithEscape")) settings.pauseGameWithEscape = true;
                if (!settingsMap.ContainsKey("workTimerEnabled")) settings.workTimerEnabled = true;
            }
            catch
            {
                settings = new GameSettings
                {
                    pollSeconds = 3,
                    gameBrightnessPercent = 75,
                    gameTimerEnabled = true,
                    gameTimerMinutes = 30,
                    breakTimerMinutes = 5,
                    workTimerEnabled = true,
                    workTimerMinutes = 30,
                    workBreakMinutes = 5,
                    pauseGameWithEscape = true,
                    gameApps = new string[0],
                    gameFolders = new string[0],
                    excludedProcesses = new string[0],
                    activeMode = "game",
                    workApps = new string[0]
                };
            }

            if (settings.gameApps == null) settings.gameApps = new string[0];
            if (settings.gameFolders == null) settings.gameFolders = new string[0];
            if (settings.excludedProcesses == null) settings.excludedProcesses = new string[0];
            if (settings.workApps == null) settings.workApps = new string[0];
            if (!string.Equals(settings.activeMode, "work", StringComparison.OrdinalIgnoreCase)) settings.activeMode = "game";
            if (settings.gameTimerMinutes < 1) settings.gameTimerMinutes = 30;
            if (settings.breakTimerMinutes < 1) settings.breakTimerMinutes = 5;
            if (settings.workTimerMinutes < 1) settings.workTimerMinutes = 30;
            if (settings.workBreakMinutes < 1) settings.workBreakMinutes = 5;
            brightnessInput.Value = Math.Max(brightnessInput.Minimum, Math.Min(brightnessInput.Maximum, settings.gameBrightnessPercent));
            pollInput.Value = Math.Max(pollInput.Minimum, Math.Min(pollInput.Maximum, settings.pollSeconds));
            timerEnabledCheck.Checked = settings.gameTimerEnabled;
            timerMinutesInput.Value = Math.Max(timerMinutesInput.Minimum, Math.Min(timerMinutesInput.Maximum, settings.gameTimerMinutes));
            breakMinutesInput.Value = Math.Max(breakMinutesInput.Minimum, Math.Min(breakMinutesInput.Maximum, settings.breakTimerMinutes));
            timerMinutesInput.Enabled = timerEnabledCheck.Checked;
            breakMinutesInput.Enabled = timerEnabledCheck.Checked;
            workTimerEnabledCheck.Checked = settings.workTimerEnabled;
            workTimerMinutesInput.Value = Math.Max(workTimerMinutesInput.Minimum, Math.Min(workTimerMinutesInput.Maximum, settings.workTimerMinutes));
            workBreakMinutesInput.Value = Math.Max(workBreakMinutesInput.Minimum, Math.Min(workBreakMinutesInput.Maximum, settings.workBreakMinutes));
            workTimerMinutesInput.Enabled = settings.workTimerEnabled;
            workBreakMinutesInput.Enabled = settings.workTimerEnabled;
            pauseWithEscapeCheck.Checked = settings.pauseGameWithEscape;
            foldersList.Items.Clear();
            foreach (string app in settings.gameApps) foldersList.Items.Add(new GameDetectionTarget(app, true));
            foreach (string folder in settings.gameFolders) foldersList.Items.Add(new GameDetectionTarget(folder, false));
            workAppsList.Items.Clear();
            foreach (string app in settings.workApps) workAppsList.Items.Add(app);
            ShowManagementMode(string.Equals(settings.activeMode, "work", StringComparison.OrdinalIgnoreCase));
            UpdateTimerConfirmState();
        }

        private void SaveSettingsAsync(string requestedMode, Action<bool> completed)
        {
            if (settingsSaveInProgress || System.Threading.Interlocked.CompareExchange(ref watcherTransitionRunning, 1, 0) != 0)
            {
                SetFooter("Please wait for the current change to finish...");
                if (completed != null) completed(false);
                return;
            }

            GameSettings previousSettings = settings;
            GameSettings next;
            try
            {
                next = json.Deserialize<GameSettings>(json.Serialize(settings));
                next.pollSeconds = (int)pollInput.Value;
                next.gameBrightnessPercent = (int)brightnessInput.Value;
                next.gameTimerEnabled = timerEnabledCheck.Checked;
                next.gameTimerMinutes = (int)timerMinutesInput.Value;
                next.breakTimerMinutes = (int)breakMinutesInput.Value;
                next.workTimerEnabled = workTimerEnabledCheck.Checked;
                next.workTimerMinutes = (int)workTimerMinutesInput.Value;
                next.workBreakMinutes = (int)workBreakMinutesInput.Value;
                next.pauseGameWithEscape = pauseWithEscapeCheck.Checked;
                GameDetectionTarget[] gameTargets = foldersList.Items.Cast<object>().OfType<GameDetectionTarget>().ToArray();
                next.gameApps = gameTargets.Where(item => item.IsApplication).Select(item => item.Path).ToArray();
                next.gameFolders = gameTargets.Where(item => !item.IsApplication).Select(item => item.Path).ToArray();
                next.activeMode = requestedMode ?? (workView ? "work" : "game");
                next.workApps = workAppsList.Items.Cast<object>().Select(item => item.ToString()).ToArray();
            }
            catch (Exception ex)
            {
                System.Threading.Interlocked.Exchange(ref watcherTransitionRunning, 0);
                ShowError("Settings could not be prepared.\r\n\r\n" + ex.Message);
                if (completed != null) completed(false);
                return;
            }

            settingsSaveInProgress = true;
            SetSettingsControlsEnabled(false);
            SetFooter("Applying settings...");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string previousContents = null;
                bool watcherStopped = false;
                bool handoffRequested = false;
                bool wasRunning = false;
                string error = null;
                string restoreError = null;
                try
                {
                    previousContents = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
                    wasRunning = IsWatcherRunning();
                    if (wasRunning)
                    {
                        handoffRequested = true;
                        StopWatcherForSettingsChange(previousSettings.activeMode);
                        watcherStopped = true;
                    }
                    WriteSettingsAtomically(PrettyJson(new JavaScriptSerializer().Serialize(next)));
                    if (wasRunning) StartWatcherAfterSettingsChange();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (watcherStopped || handoffRequested)
                    {
                        try
                        {
                            if (previousContents == null) File.Delete(settingsPath);
                            else WriteSettingsAtomically(previousContents);
                            if (!IsWatcherRunning()) StartWatcherAfterSettingsChange();
                            lastWatcherStartAttempt = DateTime.MinValue;
                        }
                        catch (Exception restoreEx) { restoreError = restoreEx.Message; }
                    }
                }

                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        settingsSaveInProgress = false;
                        System.Threading.Interlocked.Exchange(ref watcherTransitionRunning, 0);
                        SetSettingsControlsEnabled(true);
                        bool saved = string.IsNullOrEmpty(error);
                        if (saved)
                        {
                            settings = next;
                            System.Threading.Interlocked.Increment(ref statusRefreshGeneration);
                            SetFooter(wasRunning ? "Settings saved; watcher restarted." : "Settings saved.");
                            UpdateTimerConfirmState();
                            if (!modeSwitchInProgress) RefreshStatus();
                        }
                        else
                        {
                            settings = previousSettings;
                            if (!string.IsNullOrEmpty(restoreError))
                                ShowError("The previous watcher could not be restarted. Please use Enable after checking the log.\r\n\r\n" + restoreError);
                            ShowError("Settings could not be saved.\r\n\r\n" + error);
                        }
                        if (completed != null) completed(saved);
                    }));
                }
                catch
                {
                    settingsSaveInProgress = false;
                    System.Threading.Interlocked.Exchange(ref watcherTransitionRunning, 0);
                }
            });
        }

        private void SetSettingsControlsEnabled(bool enabled)
        {
            if (gameSurface != null) gameSurface.Enabled = enabled;
            if (workSurface != null) workSurface.Enabled = enabled;
            UseWaitCursor = !enabled;
        }

        private void StopWatcherForSettingsChange(string oldMode)
        {
            string requestPath = Path.Combine(rootPath, "mode-switch-request.json");
            string ackPath = Path.Combine(rootPath, "mode-switch-ack.json");
            string requestId = Guid.NewGuid().ToString("N");
            string temporaryRequestPath = Path.Combine(rootPath, "mode-switch-request." + requestId + ".tmp");
            int timeoutMs = Math.Max(15000, (Math.Max(2, settings.pollSeconds) + 8) * 1000);
            int requesterPid;
            long requesterStartTicks;
            using (Process requester = Process.GetCurrentProcess())
            {
                requesterPid = requester.Id;
                requesterStartTicks = requester.StartTime.ToUniversalTime().Ticks;
            }
            if (File.Exists(ackPath)) File.Delete(ackPath);
            bool finished = false;
            bool canceled = false;
            JavaScriptSerializer operationJson = new JavaScriptSerializer();
            try
            {
                File.WriteAllText(temporaryRequestPath, operationJson.Serialize(new
                {
                    RequestId = requestId,
                    Mode = oldMode,
                    RequesterPid = requesterPid,
                    RequesterStartTicks = requesterStartTicks,
                    ExpiresUtc = DateTime.UtcNow.AddMilliseconds(timeoutMs + 60000).ToString("o")
                }), new UTF8Encoding(false));
                using (System.Threading.Mutex transitionMutex = new System.Threading.Mutex(false, @"Local\GameManagementTransition_v1"))
                {
                    try { transitionMutex.WaitOne(); }
                    catch (System.Threading.AbandonedMutexException) { }
                    try
                    {
                        if (File.Exists(requestPath)) File.Delete(requestPath);
                        File.Move(temporaryRequestPath, requestPath);
                    }
                    finally { transitionMutex.ReleaseMutex(); }
                }
                SignalWatcherWake();
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < timeoutMs)
                {
                    if (IsMatchingModeSwitchAck(ackPath, requestId, oldMode, operationJson) && !IsWatcherRunning())
                    {
                        finished = true;
                        return;
                    }
                    System.Threading.Thread.Sleep(100);
                }
                bool acknowledged;
                using (System.Threading.Mutex transitionMutex = new System.Threading.Mutex(false, @"Local\GameManagementTransition_v1"))
                {
                    try { transitionMutex.WaitOne(); }
                    catch (System.Threading.AbandonedMutexException) { }
                    try
                    {
                        acknowledged = IsMatchingModeSwitchAck(ackPath, requestId, oldMode, operationJson);
                        if (!acknowledged)
                        {
                            if (File.Exists(requestPath)) File.Delete(requestPath);
                            canceled = true;
                        }
                    }
                    finally { transitionMutex.ReleaseMutex(); }
                }
                if (!acknowledged)
                    throw new TimeoutException("The running session did not finish its handoff. No settings were changed.");
                Stopwatch releaseWait = Stopwatch.StartNew();
                while (releaseWait.ElapsedMilliseconds < 30000)
                {
                    if (!IsWatcherRunning())
                    {
                        finished = true;
                        return;
                    }
                    System.Threading.Thread.Sleep(100);
                }
                throw new TimeoutException("The session was saved, but the watcher has not exited. Check its status before retrying.");
            }
            finally
            {
                if (File.Exists(temporaryRequestPath)) File.Delete(temporaryRequestPath);
                if (finished || canceled)
                {
                    if (File.Exists(requestPath)) File.Delete(requestPath);
                    if (File.Exists(ackPath)) File.Delete(ackPath);
                }
            }
        }

        private static void SignalWatcherWake()
        {
            try
            {
                using (System.Threading.EventWaitHandle wakeEvent =
                    System.Threading.EventWaitHandle.OpenExisting(@"Local\GameManagementWatcherWake_v1"))
                    wakeEvent.Set();
            }
            catch (System.Threading.WaitHandleCannotBeOpenedException) { }
            catch (UnauthorizedAccessException) { }
        }

        private bool IsMatchingModeSwitchAck(string ackPath, string requestId, string oldMode, JavaScriptSerializer operationJson)
        {
            try
            {
                if (!File.Exists(ackPath)) return false;
                Dictionary<string, object> ack = operationJson.Deserialize<Dictionary<string, object>>(File.ReadAllText(ackPath));
                object id, mode, saved;
                return ack.TryGetValue("RequestId", out id) &&
                    ack.TryGetValue("Mode", out mode) &&
                    ack.TryGetValue("Saved", out saved) &&
                    string.Equals(Convert.ToString(id), requestId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Convert.ToString(mode), oldMode, StringComparison.OrdinalIgnoreCase) &&
                    Convert.ToBoolean(saved);
            }
            catch { return false; }
        }

        private void StartWatcherAfterSettingsChange()
        {
            RunPowerShell(watcherPath, false);
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 10000)
            {
                if (IsWatcherRunning()) return;
                System.Threading.Thread.Sleep(100);
            }
            throw new TimeoutException("The watcher did not restart within 10 seconds.");
        }

        private void ConfirmTimerSettings()
        {
            SaveSettingsAsync(null, delegate(bool saved)
            {
                if (!saved) return;
                SetFooter("Timer confirmed: " + timerMinutesInput.Value + "-minute game / " + breakMinutesInput.Value + "-minute break.");
                RefreshTimerDisplay(IsRuntimeMode("game"));
            });
        }

        private void UpdateTimerConfirmState()
        {
            if (confirmTimerButton == null || settings == null || timerEnabledCheck == null ||
                timerMinutesInput == null || breakMinutesInput == null) return;

            confirmTimerButton.Enabled =
                timerEnabledCheck.Checked != settings.gameTimerEnabled ||
                (int)timerMinutesInput.Value != settings.gameTimerMinutes ||
                (int)breakMinutesInput.Value != settings.breakTimerMinutes;
        }

        private string PrettyJson(string compact)
        {
            int depth = 0;
            bool quoted = false;
            bool escaped = false;
            StringBuilder output = new StringBuilder();
            foreach (char character in compact)
            {
                if (escaped) { output.Append(character); escaped = false; continue; }
                if (character == '\\' && quoted) { output.Append(character); escaped = true; continue; }
                if (character == '"') quoted = !quoted;
                if (quoted) { output.Append(character); continue; }
                if (character == '{' || character == '[')
                {
                    output.Append(character).AppendLine();
                    depth++;
                    output.Append(new string(' ', depth * 2));
                }
                else if (character == '}' || character == ']')
                {
                    output.AppendLine();
                    depth--;
                    output.Append(new string(' ', depth * 2)).Append(character);
                }
                else if (character == ',')
                {
                    output.Append(character).AppendLine().Append(new string(' ', depth * 2));
                }
                else if (character == ':') output.Append(": ");
                else if (!char.IsWhiteSpace(character)) output.Append(character);
            }
            return output.ToString();
        }

        private void WriteSettingsAtomically(string contents)
        {
            string temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
                if (File.Exists(settingsPath)) File.Replace(temporaryPath, settingsPath, null, true);
                else File.Move(temporaryPath, settingsPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private void ChooseInstalledGameApplication()
        {
            using (InstalledAppsForm picker = new InstalledAppsForm())
            {
                if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedPath)) return;
                AddGameDetectionTarget(picker.SelectedPath, true);
            }
        }

        private void BrowseGameApplication()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose a game application";
                dialog.Filter = "Applications (*.exe)|*.exe";
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string path in dialog.FileNames) AddGameDetectionTarget(path, true);
            }
        }

        private void AddGameDetectionTarget(string path, bool isApplication)
        {
            string selected = isApplication ? Path.GetFullPath(path) : path.TrimEnd(Path.DirectorySeparatorChar);
            bool exists = foldersList.Items.Cast<object>().OfType<GameDetectionTarget>()
                .Any(item => string.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase));
            if (!exists) foldersList.Items.Add(new GameDetectionTarget(selected, isApplication));
            SetFooter("Game detection changed. Choose Save settings to apply it.");
        }

        private void AddFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a game-library folder. Every game installed below it will be detected.";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddGameDetectionTarget(dialog.SelectedPath, false);
            }
        }

        private void RemoveFolder()
        {
            if (foldersList.SelectedIndex >= 0)
            {
                foldersList.Items.RemoveAt(foldersList.SelectedIndex);
                SetFooter("Game detection changed. Choose Save settings to apply it.");
            }
        }

        private void EnableGameManagement()
        {
            EnableGameManagement(true);
        }

        private void EnableGameManagement(bool showMessage)
        {
            if (!File.Exists(installerPath))
            {
                ShowError("Install-GameManagement.ps1 was not found in:\r\n" + rootPath);
                return;
            }
            RunControlActionAsync("Enabling Game Management...", delegate
            {
                if (!IsWatcherRunning()) RunPowerShell(installerPath, true);
                else EnsureRunEntry();
                File.WriteAllText(engineEnabledPath, "enabled");
            }, delegate
            {
                SetFooter("Game Management enabled and set to start with Windows.");
                if (showMessage) RetroMessage("Game Management is enabled and monitoring your game folders.", "Game Management");
            }, "Game Management could not be enabled.");
        }

        private void PauseGameManagement()
        {
            PauseGameManagement(true);
        }

        private void PauseGameManagement(bool showMessage)
        {
            if (!File.Exists(pausePath))
            {
                ShowError("Pause-GameManagement.ps1 was not found in:\r\n" + rootPath);
                return;
            }
            RunControlActionAsync("Pausing Game Management...", delegate
            {
                if (File.Exists(engineEnabledPath)) File.Delete(engineEnabledPath);
                RunPowerShell(pausePath, true);
            }, delegate
            {
                SetFooter("Game Management paused; captured system settings restored.");
                if (showMessage) RetroMessage("Game Management is paused. The previous power plan and brightness were restored.", "Game Management");
            }, "Game Management could not be paused safely.");
        }

        private void SetStartup(bool enabled)
        {
            RunControlActionAsync(enabled ? "Enabling Windows startup..." : "Disabling Windows startup...", delegate
            {
                if (enabled)
                {
                    if (!IsWatcherRunning())
                    {
                        RunPowerShell(installerPath, true);
                        File.WriteAllText(engineEnabledPath, "enabled");
                    }
                    else EnsureRunEntry();
                }
                else RemoveRunEntry();
            }, delegate
            {
                SetFooter(enabled
                    ? "Windows sign-in startup enabled."
                    : "Windows sign-in startup disabled; current watcher left running.");
            }, "Startup setting could not be changed.");
        }

        private void RunControlActionAsync(string progressText, Action work, Action success, string errorPrefix)
        {
            if (settingsSaveInProgress || System.Threading.Interlocked.CompareExchange(ref watcherTransitionRunning, 1, 0) != 0)
            {
                SetFooter("Please wait for the current change to finish...");
                return;
            }
            System.Threading.Interlocked.Exchange(ref controlActionRunning, 1);
            SetSettingsControlsEnabled(false);
            SetFooter(progressText);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string error = null;
                try { work(); }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        System.Threading.Interlocked.Exchange(ref controlActionRunning, 0);
                        System.Threading.Interlocked.Exchange(ref watcherTransitionRunning, 0);
                        SetSettingsControlsEnabled(true);
                        if (string.IsNullOrEmpty(error)) success();
                        else ShowError(errorPrefix + "\r\n\r\n" + error);
                        System.Threading.Interlocked.Increment(ref statusRefreshGeneration);
                        RefreshStatus();
                    }));
                }
                catch
                {
                    System.Threading.Interlocked.Exchange(ref controlActionRunning, 0);
                    System.Threading.Interlocked.Exchange(ref watcherTransitionRunning, 0);
                }
            });
        }

        private void EnsureRunEntry()
        {
            string applicationPath = Path.Combine(rootPath, "GameManagement.exe");
            string command = "\"" + applicationPath + "\" --ui-start-hidden";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                key.SetValue("GameManagementEngine", command, RegistryValueKind.String);
                key.DeleteValue("CodexGameManagement", false);
            }
        }

        private void RemoveRunEntry()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null) return;
                key.DeleteValue("GameManagementEngine", false);
                key.DeleteValue("CodexGameManagement", false);
            }
        }

        private void RunPowerShell(string script, bool wait)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "powershell.exe";
            info.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
            info.WorkingDirectory = rootPath;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            using (Process process = Process.Start(info))
            {
                if (wait)
                {
                    if (!process.WaitForExit(15000)) throw new TimeoutException("The control script did not finish within 15 seconds.");
                    if (process.ExitCode != 0) throw new InvalidOperationException("The control script returned exit code " + process.ExitCode + ".");
                }
            }
        }

        private void RefreshStatus()
        {
            if (IsDisposed || !IsHandleCreated || modeSwitchInProgress) return;
            if (System.Threading.Interlocked.Exchange(ref statusRefreshRunning, 1) != 0)
            {
                System.Threading.Interlocked.Exchange(ref statusRefreshPending, 1);
                return;
            }
            bool collectFullStatus = Visible;
            bool collectWorkStatus = workView;
            bool collectRecentLog = activityVisible;
            int generation = System.Threading.Interlocked.Increment(ref statusRefreshGeneration);
            string configuredMode = settings == null ? "game" : settings.activeMode;
            string[] selectedWorkApps = settings == null || settings.workApps == null
                ? new string[0]
                : settings.workApps.ToArray();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                ManagementStatusSnapshot snapshot = CollectStatusSnapshot(
                    generation, collectFullStatus, collectWorkStatus, collectRecentLog, configuredMode, selectedWorkApps);
                try
                {
                    BeginInvoke(new MethodInvoker(delegate { ApplyStatusSnapshot(snapshot); }));
                }
                catch
                {
                    System.Threading.Interlocked.Exchange(ref statusRefreshRunning, 0);
                }
            });
        }

        private ManagementStatusSnapshot CollectStatusSnapshot(
            int generation, bool collectFullStatus, bool collectWorkStatus, bool collectRecentLog,
            string configuredMode, string[] selectedWorkApps)
        {
            ManagementStatusSnapshot snapshot = new ManagementStatusSnapshot();
            snapshot.Generation = generation;
            try
            {
                snapshot.Watcher = IsWatcherRunning();
                bool engineEnabled = File.Exists(engineEnabledPath);
                if (engineEnabled && !snapshot.Watcher && DateTime.Now - lastWatcherStartAttempt > TimeSpan.FromSeconds(10) &&
                    System.Threading.Interlocked.CompareExchange(ref watcherTransitionRunning, 1, 0) == 0)
                {
                    try
                    {
                        if (File.Exists(engineEnabledPath) && !IsWatcherRunning())
                        {
                            lastWatcherStartAttempt = DateTime.Now;
                            RunPowerShell(watcherPath, false);
                            snapshot.Restarting = true;
                        }
                    }
                    finally { System.Threading.Interlocked.Exchange(ref watcherTransitionRunning, 0); }
                }
                string runtimeMode = null;
                Dictionary<string, object> runtimeState = null;
                if (File.Exists(statePath))
                {
                    try
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        runtimeState = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
                        object mode;
                        if (runtimeState.TryGetValue("Mode", out mode)) runtimeMode = Convert.ToString(mode);
                    }
                    catch { }
                }
                snapshot.StateActive = string.Equals(runtimeMode, "game", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(runtimeMode, "work", StringComparison.OrdinalIgnoreCase);
                if (!collectFullStatus) return snapshot;

                snapshot.Full = true;
                bool gameStateActive = string.Equals(runtimeMode, "game", StringComparison.OrdinalIgnoreCase);
                string activeSchemeOutput = RunCapture("powercfg.exe", "/getactivescheme");
                string activeSchemeGuid = GetActiveSchemeGuid(activeSchemeOutput);
                snapshot.Managed = gameStateActive || (runtimeMode == null &&
                    string.Equals(configuredMode, "game", StringComparison.OrdinalIgnoreCase) &&
                    activeSchemeGuid == ManagementPlanGuid);
                snapshot.ActivePlan = GetActiveSchemeName(activeSchemeOutput, activeSchemeGuid);
                string[] logLines = (snapshot.Managed || collectRecentLog) ? ReadLogTail(200) : new string[0];
                snapshot.Game = GetActiveGameFromLines(snapshot.Managed, logLines);
                if (collectRecentLog)
                    snapshot.RecentLogLines = logLines.Skip(Math.Max(0, logLines.Length - 6)).ToArray();
                snapshot.Brightness = GetBrightness();
                snapshot.Startup = HasStartupEntry();

                if (collectWorkStatus)
                {
                    snapshot.WorkApplications = GetRunningSelectedWorkApps(selectedWorkApps);
                    if (string.Equals(runtimeMode, "work", StringComparison.OrdinalIgnoreCase) && runtimeState != null)
                    {
                        object started;
                        DateTime began;
                        if (runtimeState.TryGetValue("ManagementStarted", out started) &&
                            DateTime.TryParse(Convert.ToString(started), null, System.Globalization.DateTimeStyles.RoundtripKind, out began))
                            snapshot.WorkSessionStartedAt = began.ToLocalTime();
                    }
                }
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
            }
            return snapshot;
        }

        private void ApplyStatusSnapshot(ManagementStatusSnapshot snapshot)
        {
            System.Threading.Interlocked.Exchange(ref statusRefreshRunning, 0);
            if (exiting || IsDisposed || !IsHandleCreated) return;
            if (snapshot.Generation != System.Threading.Interlocked.CompareExchange(ref statusRefreshGeneration, 0, 0))
            {
                if (modeSwitchInProgress)
                    System.Threading.Interlocked.Exchange(ref statusRefreshPending, 1);
                else
                {
                    System.Threading.Interlocked.Exchange(ref statusRefreshPending, 0);
                    RefreshStatus();
                }
                return;
            }
            if (modeSwitchInProgress)
            {
                System.Threading.Interlocked.Exchange(ref statusRefreshPending, 1);
                return;
            }
            if (!string.IsNullOrEmpty(snapshot.Error)) SetFooter("Status refresh warning: " + snapshot.Error);
            if (snapshot.Restarting) SetFooter("Restarting the Game Management watcher...");
            TrackManagementState(snapshot.StateActive || snapshot.Managed);
            trayIcon.Text = snapshot.Watcher && !snapshot.StateActive
                ? "Game Management"
                : (snapshot.StateActive ? "Game Management — ACTIVE" : "Game Management — paused");

            if (snapshot.Full && Visible)
            {
                statusLamp.BackColor = snapshot.Managed ? Color.Lime : (snapshot.Watcher ? Color.Yellow : Color.Gray);
                statusValue.Text = snapshot.Managed ? "GAME ACTIVE" : (snapshot.Watcher ? "Ready / monitoring" : "Paused");
                gameValue.Text = snapshot.Managed ? snapshot.Game : "None";
                planValue.Text = string.IsNullOrEmpty(snapshot.ActivePlan) ? "Unavailable" : snapshot.ActivePlan;
                brightnessValue.Text = snapshot.Brightness.HasValue ? snapshot.Brightness.Value + "%" : "Unavailable";
                RefreshTimerDisplay(snapshot.Managed);
                watcherValue.Text = snapshot.Watcher ? "Running" : "Stopped";
                enableButton.Enabled = !snapshot.Watcher;
                pauseButton.Enabled = snapshot.Watcher || snapshot.Managed;
                startupCheck.Checked = snapshot.Startup;
                trayIcon.Text = snapshot.Watcher && !snapshot.Managed
                    ? "Game Management"
                    : (snapshot.Managed ? "Game Management — ACTIVE" : "Game Management — paused");
                if (workView) ApplyWorkStatus(snapshot.WorkApplications, snapshot.WorkSessionStartedAt);
                if (activityVisible) ApplyRecentLog(snapshot.RecentLogLines);
            }
            else if (Visible)
            {
                System.Threading.Interlocked.Exchange(ref statusRefreshPending, 1);
            }

            if (System.Threading.Interlocked.Exchange(ref statusRefreshPending, 0) != 0) RefreshStatus();
        }

        private string GetRuntimeMode()
        {
            if (!File.Exists(statePath)) return null;
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
                object mode;
                if (state.TryGetValue("Mode", out mode))
                {
                    string value = Convert.ToString(mode);
                    if (string.Equals(value, "work", StringComparison.OrdinalIgnoreCase)) return "work";
                    if (string.Equals(value, "game", StringComparison.OrdinalIgnoreCase)) return "game";
                }
            }
            catch { }
            return null;
        }

        private bool IsRuntimeMode(string mode)
        {
            return string.Equals(GetRuntimeMode(), mode, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsWatcherRunning()
        {
            try
            {
                bool createdNew;
                using (System.Threading.Mutex watcherMutex = new System.Threading.Mutex(true, @"Local\GameManagementWatcher_v1", out createdNew))
                {
                    if (!createdNew) return true;
                    watcherMutex.ReleaseMutex();
                }
            }
            catch { }
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name='powershell.exe' OR Name='pwsh.exe'"))
                {
                    foreach (ManagementObject process in searcher.Get())
                    {
                        string command = Convert.ToString(process["CommandLine"]);
                        if (!string.IsNullOrEmpty(command))
                        {
                            int fileArgument = command.IndexOf("-File", StringComparison.OrdinalIgnoreCase);
                            int watcherArgument = command.IndexOf(watcherPath, StringComparison.OrdinalIgnoreCase);
                            if (fileArgument >= 0 && watcherArgument > fileArgument && watcherArgument - fileArgument < 20)
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void TrackManagementState(bool managed)
        {
            if (managementStateKnown && managed == previousManagementState) return;
            if (managed) ResetTemperaturePeaks();
            previousManagementState = managed;
            managementStateKnown = true;
        }

        private static string GetActiveSchemeGuid(string output)
        {
            int marker = output.IndexOf("GUID:", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return string.Empty;
            string tail = output.Substring(marker + 5).Trim();
            return tail.Length >= 36 ? tail.Substring(0, 36).ToLowerInvariant() : string.Empty;
        }

        private static string GetActiveSchemeName(string output, string fallbackGuid)
        {
            int open = output.LastIndexOf('(');
            int close = output.LastIndexOf(')');
            if (open >= 0 && close > open) return output.Substring(open + 1, close - open - 1).Trim();
            return fallbackGuid;
        }

        private string RunCapture(string file, string arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo(file, arguments);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                return output.Trim();
            }
        }

        private int? GetBrightness()
        {
            try
            {
                ManagementScope scope = new ManagementScope(@"\\.\root\WMI");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CurrentBrightness, Active FROM WmiMonitorBrightness")))
                {
                    foreach (ManagementObject monitor in searcher.Get())
                    {
                        if (Convert.ToBoolean(monitor["Active"])) return Convert.ToInt32(monitor["CurrentBrightness"]);
                    }
                }
            }
            catch { }
            return null;
        }

        private string GetActiveGameFromLines(bool managed, string[] logLines)
        {
            if (!managed) return "None";
            string line = (logLines ?? new string[0]).Reverse().FirstOrDefault(item => item.IndexOf("Game Management ON", StringComparison.OrdinalIgnoreCase) >= 0);
            if (line == null) return "Detected game";
            int marker = line.IndexOf("games:", StringComparison.OrdinalIgnoreCase);
            return marker >= 0 ? line.Substring(marker + 6).Trim() : "Detected game";
        }

        private void RefreshTimerDisplay(bool managed)
        {
            if (timerValue == null || minutesLeftValue == null || cycleValue == null) return;
            if (settings == null || !settings.gameTimerEnabled)
            {
                SetLabelText(timerValue, "Off");
                SetLabelText(minutesLeftValue, "--:--");
                SetLabelText(cycleValue, "0");
                return;
            }
            if (!managed)
            {
                int readyMinutes = Math.Max(1, settings.gameTimerMinutes);
                SetLabelText(timerValue, "Ready");
                SetLabelText(minutesLeftValue, readyMinutes.ToString("00") + ":00");
                SetLabelText(cycleValue, "0");
                return;
            }
            if (!File.Exists(timerStatePath))
            {
                SetLabelText(timerValue, "Starting");
                SetLabelText(minutesLeftValue, Math.Max(1, settings.gameTimerMinutes).ToString("00") + ":00");
                SetLabelText(cycleValue, "1");
                return;
            }
            try
            {
                Dictionary<string, object> timerState = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(timerStatePath));
                object timerMode;
                if (!timerState.TryGetValue("Mode", out timerMode) ||
                    !string.Equals(Convert.ToString(timerMode), "game", StringComparison.OrdinalIgnoreCase))
                {
                    RefreshTimerDisplay(false);
                    return;
                }
                object deadlineValue;
                if (!timerState.TryGetValue("Deadline", out deadlineValue)) throw new InvalidDataException();
                object phaseValue;
                string phase = timerState.TryGetValue("Phase", out phaseValue) ? Convert.ToString(phaseValue) : "Game";
                object cycleValueObject;
                int cycle = timerState.TryGetValue("Cycle", out cycleValueObject) ? Math.Max(1, Convert.ToInt32(cycleValueObject)) : 1;
                DateTime deadline;
                if (!DateTime.TryParse(Convert.ToString(deadlineValue), null, System.Globalization.DateTimeStyles.RoundtripKind, out deadline))
                    throw new InvalidDataException();
                TimeSpan remaining = deadline.ToLocalTime() - DateTime.Now;
                int totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
                SetLabelText(timerValue, string.Equals(phase, "Break", StringComparison.OrdinalIgnoreCase) ? "Break" : "Game");
                SetLabelText(cycleValue, cycle.ToString());
                if (totalSeconds <= 0)
                {
                    SetLabelText(minutesLeftValue, "00:00");
                    return;
                }

                int displayMinutes = totalSeconds / 60;
                int displaySeconds = totalSeconds % 60;
                SetLabelText(minutesLeftValue, string.Format("{0:00}:{1:00}", displayMinutes, displaySeconds));
            }
            catch
            {
                SetLabelText(timerValue, "Unavailable");
                SetLabelText(minutesLeftValue, "--:--");
                SetLabelText(cycleValue, "—");
            }
        }

        private static void SetLabelText(Label label, string text)
        {
            if (!string.Equals(label.Text, text, StringComparison.Ordinal)) label.Text = text;
        }

        private bool HasStartupEntry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue("GameManagementEngine") != null;
            }
            catch { return false; }
        }

        private string[] ReadLogTail(int maximumLines)
        {
            if (!File.Exists(logPath) || maximumLines <= 0) return new string[0];
            try
            {
                const int maximumBytes = 262144;
                using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, stream.Length - maximumBytes);
                    stream.Position = start;
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
                    {
                        if (start > 0) reader.ReadLine();
                        Queue<string> tail = new Queue<string>(maximumLines);
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (tail.Count == maximumLines) tail.Dequeue();
                            tail.Enqueue(line);
                        }
                        return tail.ToArray();
                    }
                }
            }
            catch { return new string[0]; }
        }

        private void ApplyRecentLog(string[] lines)
        {
            logBox.Lines = lines != null && lines.Length > 0 ? lines : new[] { "No activity has been logged yet." };
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void OpenLog()
        {
            if (!File.Exists(logPath)) { RetroMessage("The activity log has not been created yet.", "Game Management"); return; }
            Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        }

        private void OpenDiagnostics()
        {
            string report = Path.Combine(rootPath, "SYSTEM-POWER-AUDIT.md");
            if (!File.Exists(report)) report = Path.Combine(rootPath, "SECURITY-REPORT.md");
            if (!File.Exists(report)) { RetroMessage("No diagnostic report was found.", "Game Management"); return; }
            Process.Start(new ProcessStartInfo(report) { UseShellExecute = true });
        }


        private void HideToTray()
        {
            if (!trayCheck.Checked) { ExitApplication(); return; }
            Hide();
            ShowInTaskbar = false;
        }

        private void ShowFromTray(bool keepOnTop = false)
        {
            bool wasTopMost = TopMost;
            TopMost = true;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();

            if (!keepOnTop && !wasTopMost)
            {
                Timer foregroundTimer = new Timer();
                foregroundTimer.Interval = 250;
                foregroundTimer.Tick += delegate
                {
                    foregroundTimer.Stop();
                    foregroundTimer.Dispose();
                    if (IsDisposed) return;
                    TopMost = false;
                    BringToFront();
                    Activate();
                };
                foregroundTimer.Start();
            }

            RefreshStatus();
            RefreshTemperatureMetrics();
        }

        private void ExitApplication()
        {
            if (IsWatcherTransitionActive())
            {
                ExplainBlockedExit();
                return;
            }
            exiting = true;
            refreshTimer.Stop();
            countdownTimer.Stop();
            performanceTimer.Stop();
            panelFlipTimer.Stop();
            trayIcon.Visible = false;
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing && IsWatcherTransitionActive())
            {
                e.Cancel = true;
                ExplainBlockedExit();
                return;
            }
            if (!exiting && trayCheck.Checked)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            trayIcon.Dispose();
            refreshTimer.Dispose();
            countdownTimer.Dispose();
            performanceTimer.Dispose();
            panelFlipTimer.Dispose();
            stopUiSignalThread = true;
            if (showMainEvent != null) showMainEvent.Set();
            if (uiSignalThread != null && uiSignalThread.IsAlive) uiSignalThread.Join(1000);
            if (showMainEvent != null) showMainEvent.Dispose();
            if (showBreakEvent != null) showBreakEvent.Dispose();
            if (hideBreakEvent != null) hideBreakEvent.Dispose();
        }

        private bool IsWatcherTransitionActive()
        {
            return System.Threading.Interlocked.CompareExchange(ref watcherTransitionRunning, 0, 0) != 0 ||
                settingsSaveInProgress || System.Threading.Interlocked.CompareExchange(ref controlActionRunning, 0, 0) != 0;
        }

        private void ExplainBlockedExit()
        {
            SetFooter("Please wait—Game Management is finishing a change.");
            RetroMessage("Game Management is finishing a change. Please wait a moment, then try Exit again.", "Game Management");
        }

        private void SetFooter(string text)
        {
            footerLabel.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
        }

        private void RetroMessage(string message, string title)
        {
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Game Management", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
