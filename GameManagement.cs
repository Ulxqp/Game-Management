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
            string heading = breakFinished ? "Break over!" : "Time for a break!";
            string timerName = breakFinished ? "Game timer" : "Break timer";
            string message = breakFinished ? "You can play again." : "Your game time is up.";

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
        public bool pauseGameWithEscape { get; set; }
        public string[] gameFolders { get; set; }
        public string[] excludedProcesses { get; set; }
    }

    internal sealed class GameSessionSummary
    {
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

            Label heading = new Label
            {
                Text = "GAME SESSION FINISHED",
                Location = new Point(20, 14),
                Size = new Size(445, 31),
                Font = new Font("Arial", 17F, FontStyle.Bold),
                ForeColor = Color.Black
            };
            ContentPanel.Controls.Add(heading);

            string details =
                "Game: " + Safe(summary.Game) + Environment.NewLine +
                "Played: " + Safe(summary.DurationText) + Environment.NewLine +
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
        private readonly string performanceStatePath;
        private readonly string engineEnabledPath;
        private readonly string watcherPath;
        private readonly string installerPath;
        private readonly string pausePath;

        private readonly Timer refreshTimer = new Timer();
        private readonly Timer countdownTimer = new Timer();
        private readonly Timer performanceTimer = new Timer();
        private readonly Timer panelFlipTimer = new Timer();
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
        private bool activityVisible = true;
        private int expandedClientHeight = 720;
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
                if (Visible) RefreshTimerDisplay(File.Exists(statePath) || File.Exists(timerStatePath));
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
            ClientSize = new Size(820, 720);
            MinimumSize = new Size(720, 680);
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

            Label brand = new Label();
            brand.Text = "GAME MANAGEMENT";
            brand.Font = new Font("Arial", 26F, FontStyle.Bold);
            brand.AutoSize = true;
            brand.Location = new Point(17, 12);
            body.Controls.Add(brand);

            Label subtitle = new Label();
            subtitle.Text = "Automatic game management and break controller";
            subtitle.AutoSize = true;
            subtitle.Location = new Point(21, 53);
            body.Controls.Add(subtitle);

            statusGroup = RetroGroup("Current status", 20, 82, 375, 248);
            body.Controls.Add(statusGroup);

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
            body.Controls.Add(enableButton);

            pauseButton = RetroButton("Pause", 92, 26);
            pauseButton.Location = new Point(520, 93);
            pauseButton.Click += delegate { PauseGameManagement(); };
            body.Controls.Add(pauseButton);

            Button refreshButton = RetroButton("Refresh", 92, 26);
            refreshButton.Location = new Point(622, 93);
            refreshButton.Click += delegate { RefreshStatus(); };
            body.Controls.Add(refreshButton);

            GroupBox settingsGroup = RetroGroup("Settings", 414, 132, 370, 184);
            body.Controls.Add(settingsGroup);
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

            GroupBox foldersGroup = RetroGroup("Game library folders", 20, 330, 764, 142);
            foldersGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            body.Controls.Add(foldersGroup);
            foldersList = new ListBox();
            foldersList.Location = new Point(13, 23);
            foldersList.Size = new Size(628, 95);
            foldersList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            foldersList.HorizontalScrollbar = true;
            foldersGroup.Controls.Add(foldersList);

            Button addFolder = RetroButton("Add...", 92, 25);
            addFolder.Location = new Point(654, 23);
            addFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            addFolder.Click += delegate { AddFolder(); };
            foldersGroup.Controls.Add(addFolder);

            Button removeFolder = RetroButton("Remove", 92, 25);
            removeFolder.Location = new Point(654, 56);
            removeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            removeFolder.Click += delegate { RemoveFolder(); };
            foldersGroup.Controls.Add(removeFolder);

            Button saveButton = RetroButton("Save settings", 92, 25);
            saveButton.Location = new Point(654, 89);
            saveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveButton.Click += delegate { SaveSettings(); };
            foldersGroup.Controls.Add(saveButton);

            logGroup = RetroGroup("Recent activity", 20, 484, 764, 111);
            logGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            body.Controls.Add(logGroup);
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
            body.Controls.Add(trayCheck);

            toggleLogButton = RetroButton("Hide activity", 92, 25);
            toggleLogButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            toggleLogButton.Click += delegate { ToggleActivity(); };
            body.Controls.Add(toggleLogButton);

            footerLabel = new Label();
            footerLabel.Text = "Ready.";
            footerLabel.BorderStyle = BorderStyle.Fixed3D;
            footerLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            footerLabel.Location = new Point(20, 582);
            footerLabel.Size = new Size(764, 20);
            footerLabel.TextAlign = ContentAlignment.MiddleLeft;
            body.Controls.Add(footerLabel);

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
                int folderActionWidth = Math.Max(92, Math.Min(130, foldersGroup.ClientSize.Width / 7));
                int folderActionLeft = foldersGroup.ClientSize.Width - folderPadding - folderActionWidth;
                foldersList.Width = Math.Max(220, folderActionLeft - foldersList.Left - folderGap);
                addFolder.Left = folderActionLeft;
                addFolder.Width = folderActionWidth;
                removeFolder.Left = folderActionLeft;
                removeFolder.Width = folderActionWidth;
                saveButton.Left = folderActionLeft;
                saveButton.Width = folderActionWidth;

                footerLabel.Top = body.ClientSize.Height - footerLabel.Height - 7;
                footerLabel.Width = contentWidth;
                trayCheck.Top = footerLabel.Top - trayCheck.Height - 4;
                toggleLogButton.Top = trayCheck.Top - 4;
                logGroup.Height = Math.Max(82, trayCheck.Top - logGroup.Top - 8);

                int logPadding = 13;
                int logGap = 10;
                int actionWidth = Math.Max(92, Math.Min(130, logGroup.ClientSize.Width / 7));
                int actionLeft = logGroup.ClientSize.Width - logPadding - actionWidth;
                toggleLogButton.Left = logGroup.Left + actionLeft;
                toggleLogButton.Width = actionWidth;
                toggleLogButton.Height = 25;
                logBox.Width = Math.Max(220, actionLeft - logBox.Left - logGap);
                openLog.Left = actionLeft;
                openLog.Width = actionWidth;
                openLog.Height = 25;
                diagnostics.Left = actionLeft;
                diagnostics.Top = openLog.Top + 33;
                diagnostics.Width = actionWidth;
                diagnostics.Height = 25;
                logBox.Top = openLog.Top;
                logBox.Height = diagnostics.Bottom - logBox.Top;
            };

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                StartUiSignalListener();
                RefreshStatus();
                if (enableOnStart && !IsWatcherRunning()) EnableGameManagement(false);
                if (showForBreakOnStart) ShowForBreak();
                else if (hideOnStart) BeginInvoke(new MethodInvoker(HideAfterBreak));
                if (Visible || File.Exists(statePath)) RefreshTemperatureMetrics();
            };
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
            if (!Visible && !File.Exists(statePath))
            {
                if (File.Exists(performanceStatePath)) File.Delete(performanceStatePath);
                return;
            }
            float? cpuTemperature = ReadCpuTemperature();
            float? gpuTemperature = ReadGpuTemperature();
            if (cpuTemperature.HasValue)
                peakCpuTemperature = !peakCpuTemperature.HasValue ? cpuTemperature : Math.Max(peakCpuTemperature.Value, cpuTemperature.Value);
            if (gpuTemperature.HasValue)
                peakGpuTemperature = !peakGpuTemperature.HasValue ? gpuTemperature : Math.Max(peakGpuTemperature.Value, gpuTemperature.Value);

            SetTemperatureLabel(cpuTemperatureValue, cpuTemperature, peakCpuTemperature, true);
            SetTemperatureLabel(gpuTemperatureValue, gpuTemperature, peakGpuTemperature, false);
            WriteTemperatureSnapshot(cpuTemperature, gpuTemperature);
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
                expandedClientHeight = Math.Max(720, ClientSize.Height);
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
                MinimumSize = new Size(720, 680);
                ClientSize = new Size(ClientSize.Width, Math.Max(720, expandedClientHeight));
                SetFooter("Recent activity shown.");
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
                    pauseGameWithEscape = true,
                    gameFolders = new string[0],
                    excludedProcesses = new string[0]
                };
            }

            if (settings.gameFolders == null) settings.gameFolders = new string[0];
            if (settings.excludedProcesses == null) settings.excludedProcesses = new string[0];
            if (settings.gameTimerMinutes < 1) settings.gameTimerMinutes = 30;
            if (settings.breakTimerMinutes < 1) settings.breakTimerMinutes = 5;
            brightnessInput.Value = Math.Max(brightnessInput.Minimum, Math.Min(brightnessInput.Maximum, settings.gameBrightnessPercent));
            pollInput.Value = Math.Max(pollInput.Minimum, Math.Min(pollInput.Maximum, settings.pollSeconds));
            timerEnabledCheck.Checked = settings.gameTimerEnabled;
            timerMinutesInput.Value = Math.Max(timerMinutesInput.Minimum, Math.Min(timerMinutesInput.Maximum, settings.gameTimerMinutes));
            breakMinutesInput.Value = Math.Max(breakMinutesInput.Minimum, Math.Min(breakMinutesInput.Maximum, settings.breakTimerMinutes));
            timerMinutesInput.Enabled = timerEnabledCheck.Checked;
            breakMinutesInput.Enabled = timerEnabledCheck.Checked;
            pauseWithEscapeCheck.Checked = settings.pauseGameWithEscape;
            foldersList.Items.Clear();
            foreach (string folder in settings.gameFolders) foldersList.Items.Add(folder);
            UpdateTimerConfirmState();
        }

        private bool SaveSettings()
        {
            try
            {
                settings.pollSeconds = (int)pollInput.Value;
                settings.gameBrightnessPercent = (int)brightnessInput.Value;
                settings.gameTimerEnabled = timerEnabledCheck.Checked;
                settings.gameTimerMinutes = (int)timerMinutesInput.Value;
                settings.breakTimerMinutes = (int)breakMinutesInput.Value;
                settings.pauseGameWithEscape = pauseWithEscapeCheck.Checked;
                settings.gameFolders = foldersList.Items.Cast<object>().Select(item => item.ToString()).ToArray();
                string serialized = json.Serialize(settings);
                File.WriteAllText(settingsPath, PrettyJson(serialized), new UTF8Encoding(false));
                bool wasRunning = IsWatcherRunning();
                if (wasRunning)
                {
                    PauseGameManagement(false);
                    EnableGameManagement(false);
                }
                SetFooter(wasRunning ? "Settings saved; watcher restarted." : "Settings saved.");
                UpdateTimerConfirmState();
                RefreshStatus();
                return true;
            }
            catch (Exception ex)
            {
                ShowError("Settings could not be saved.\r\n\r\n" + ex.Message);
                return false;
            }
        }

        private void ConfirmTimerSettings()
        {
            if (!SaveSettings()) return;
            SetFooter("Timer confirmed: " + timerMinutesInput.Value + "-minute game / " + breakMinutesInput.Value + "-minute break.");
            RefreshTimerDisplay(File.Exists(statePath) || File.Exists(timerStatePath));
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

        private void AddFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a game-library folder. Every game installed below it will be detected.";
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string selected = dialog.SelectedPath.TrimEnd(Path.DirectorySeparatorChar);
                bool exists = foldersList.Items.Cast<object>().Any(item => string.Equals(item.ToString(), selected, StringComparison.OrdinalIgnoreCase));
                if (!exists) foldersList.Items.Add(selected);
            }
        }

        private void RemoveFolder()
        {
            if (foldersList.SelectedIndex >= 0) foldersList.Items.RemoveAt(foldersList.SelectedIndex);
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
            try
            {
                RunPowerShell(installerPath, true);
                File.WriteAllText(engineEnabledPath, "enabled");
                SetFooter("Game Management enabled and set to start with Windows.");
                if (showMessage) RetroMessage("Game Management is enabled and monitoring your game folders.", "Game Management");
            }
            catch (Exception ex) { ShowError("Game Management could not be enabled.\r\n\r\n" + ex.Message); }
            RefreshStatus();
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
            try
            {
                if (File.Exists(engineEnabledPath)) File.Delete(engineEnabledPath);
                RunPowerShell(pausePath, true);
                SetFooter("Game Management paused; captured system settings restored.");
                if (showMessage) RetroMessage("Game Management is paused. The previous power plan and brightness were restored.", "Game Management");
            }
            catch (Exception ex) { ShowError("Game Management could not be paused safely.\r\n\r\n" + ex.Message); }
            RefreshStatus();
        }

        private void SetStartup(bool enabled)
        {
            if (enabled)
            {
                if (!IsWatcherRunning()) EnableGameManagement(false);
                else EnsureRunEntry();
            }
            else
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (key != null)
                        {
                            key.DeleteValue("GameManagementEngine", false);
                            key.DeleteValue("CodexGameManagement", false);
                        }
                    }
                    SetFooter("Windows sign-in startup disabled; current watcher left running.");
                }
                catch (Exception ex) { ShowError("Startup setting could not be changed.\r\n\r\n" + ex.Message); }
            }
            RefreshStatus();
        }

        private void EnsureRunEntry()
        {
            string command = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + watcherPath + "\"";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                key.SetValue("GameManagementEngine", command, RegistryValueKind.String);
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
            Process process = Process.Start(info);
            if (wait)
            {
                if (!process.WaitForExit(15000)) throw new TimeoutException("The control script did not finish within 15 seconds.");
                if (process.ExitCode != 0) throw new InvalidOperationException("The control script returned exit code " + process.ExitCode + ".");
            }
        }

        private void RefreshStatus()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                bool watcher = IsWatcherRunning();
                bool engineEnabled = File.Exists(engineEnabledPath);
                if (engineEnabled && !watcher && DateTime.Now - lastWatcherStartAttempt > TimeSpan.FromSeconds(10))
                {
                    lastWatcherStartAttempt = DateTime.Now;
                    RunPowerShell(watcherPath, false);
                    SetFooter("Restarting the Game Management watcher...");
                }
                bool stateActive = File.Exists(statePath);
                if (!Visible)
                {
                    TrackManagementState(stateActive);
                    trayIcon.Text = watcher && !stateActive ? "Game Management" : (stateActive ? "Game Management — ACTIVE" : "Game Management — paused");
                    return;
                }
                string activeSchemeOutput = RunCapture("powercfg.exe", "/getactivescheme");
                string activeSchemeGuid = GetActiveSchemeGuid(activeSchemeOutput);
                bool managed = stateActive || activeSchemeGuid == ManagementPlanGuid;
                TrackManagementState(managed);
                string activePlan = GetActiveSchemeName(activeSchemeOutput, activeSchemeGuid);
                string game = GetActiveGameFromLog(managed);
                int? brightness = GetBrightness();
                bool startup = HasStartupEntry();

                statusLamp.BackColor = managed ? Color.Lime : (watcher ? Color.Yellow : Color.Gray);
                statusValue.Text = managed ? "GAME ACTIVE" : (watcher ? "Ready / monitoring" : "Paused");
                gameValue.Text = managed ? game : "None";
                planValue.Text = string.IsNullOrEmpty(activePlan) ? "Unavailable" : activePlan;
                brightnessValue.Text = brightness.HasValue ? brightness.Value + "%" : "Unavailable";
                RefreshTimerDisplay(managed);
                watcherValue.Text = watcher ? "Running" : "Stopped";
                enableButton.Enabled = !watcher;
                pauseButton.Enabled = watcher || managed;
                startupCheck.Checked = startup;
                trayIcon.Text = watcher && !managed ? "Game Management" : (managed ? "Game Management — ACTIVE" : "Game Management — paused");
                if (activityVisible) ReadRecentLog();
            }
            catch (Exception ex)
            {
                SetFooter("Status refresh warning: " + ex.Message);
            }
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

        private string GetActiveGameFromLog(bool managed)
        {
            if (!managed || !File.Exists(logPath)) return "None";
            try
            {
                string line = File.ReadLines(logPath).Reverse().FirstOrDefault(item => item.IndexOf("Game Management ON", StringComparison.OrdinalIgnoreCase) >= 0);
                if (line == null) return "Detected game";
                int marker = line.IndexOf("games:", StringComparison.OrdinalIgnoreCase);
                return marker >= 0 ? line.Substring(marker + 6).Trim() : "Detected game";
            }
            catch { return "Detected game"; }
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

        private void ReadRecentLog()
        {
            if (!File.Exists(logPath)) { logBox.Text = "No activity has been logged yet."; return; }
            try
            {
                string[] lines = File.ReadAllLines(logPath);
                logBox.Lines = lines.Skip(Math.Max(0, lines.Length - 6)).ToArray();
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            }
            catch { }
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
