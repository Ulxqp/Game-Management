using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyVersion("5.0.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("5.0.2.0")]

namespace GameManagement
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Any(argument => string.Equals(argument, "--preview", StringComparison.OrdinalIgnoreCase)) || (args.Length > 0 && args[0] == "--render"))
            {
                MainForm.PreviewMode = true;
                MainForm.PreviewRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-data");
                Directory.CreateDirectory(MainForm.PreviewRoot);
            }
            if (args.Length > 0 && args[0] == "--preview")
            {
                string rootArgument = args.FirstOrDefault(argument => argument.StartsWith("--preview-root=", StringComparison.OrdinalIgnoreCase));
                if (rootArgument != null)
                {
                    string baseRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string requestedRoot = Path.GetFullPath(Path.Combine(baseRoot, rootArgument.Substring(15)));
                    if (!requestedRoot.StartsWith(baseRoot, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("The preview data root must stay inside the application project.");
                    MainForm.PreviewRoot = requestedRoot;
                    Directory.CreateDirectory(MainForm.PreviewRoot);
                }
                using (MainForm form = new MainForm(false))
                {
                    int width, height;
                    if (args.Length >= 3 && int.TryParse(args[1], out width) && int.TryParse(args[2], out height))
                        form.ClientSize = new Size(Math.Max(720, width), Math.Max(560, height));
                    form.Shown += delegate
                    {
                        foreach (string argument in args)
                        {
                            if (argument.StartsWith("--page=")) form.SetPageForPreview(argument.Substring(7));
                            if (argument.StartsWith("--panel=")) form.SetOverlayForPreview(argument.Substring(8));
                        }
                        if (args.Any(argument => string.Equals(argument, "--work-mode", StringComparison.OrdinalIgnoreCase))) form.SetWorkViewForPreview(true);
                        if (args.Any(argument => string.Equals(argument, "--game-mode", StringComparison.OrdinalIgnoreCase))) form.SetWorkViewForPreview(false);
                    };
                    Application.Run(form);
                }
                return;
            }
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
                bool showPopup = true;
                bool playSound = true;
                string customAlarmPath = "";
                try
                {
                    string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                    if (File.Exists(settingsPath))
                    {
                        JavaScriptSerializer serializer = new JavaScriptSerializer();
                        Dictionary<string, object> values = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(settingsPath));
                        object value;
                        if (values.TryGetValue("timerPopupEnabled", out value)) showPopup = Convert.ToBoolean(value);
                        if (values.TryGetValue("timerSoundEnabled", out value)) playSound = Convert.ToBoolean(value);
                        if (values.TryGetValue("customAlarmPath", out value)) customAlarmPath = Convert.ToString(value) ?? "";
                    }
                }
                catch { }
                if (!showPopup)
                {
                    if (playSound) TimerAlertForm.PlayOneAlarmSound(customAlarmPath);
                    return;
                }
                Application.Run(new TimerAlertForm(alertType, finishedMinutes, nextMinutes, false, playSound, customAlarmPath));
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
                    foreach (string argument in args) { if (argument.StartsWith("--page=")) form.SetPageForPreview(argument.Substring(7)); if (argument.StartsWith("--panel=")) form.SetOverlayForPreview(argument.Substring(8)); }
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
                    if (args.Any(argument => string.Equals(argument, "--game-mode", StringComparison.OrdinalIgnoreCase)))
                    {
                        form.SetWorkViewForPreview(false);
                        Application.DoEvents();
                    }
                    using (Bitmap preview = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                        using (Graphics graphics = Graphics.FromImage(preview)) form.DrawOverlayForPreview(graphics);
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

    // Retained name keeps the existing picker/dialog API compatible.
    internal sealed class RetroDialogButton : ModernButton
    {
        public RetroDialogButton() { Font = ModernTheme.Font(9, FontStyle.Bold); }
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
            BackColor = ModernTheme.Background;
            ForeColor = ModernTheme.Text;
            Font = ModernTheme.Font(9);
            Padding = new Padding(1);
            ClientSize = new Size(ClientSize.Width, ClientSize.Height + 20);
            AutoScaleMode = AutoScaleMode.None;

            Panel titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = ModernTheme.Surface,
                Padding = new Padding(14, 0, 10, 0)
            };
            titleBar.MouseDown += DragWindow;

            Label title = new Label
            {
                Text = caption,
                ForeColor = ModernTheme.Text,
                Font = ModernTheme.Font(10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 14)
            };
            title.MouseDown += DragWindow;
            titleBar.Controls.Add(title);

            Button close = RetroButton("", ModernTheme.CompactHeight, ModernTheme.CompactHeight);
            ((ModernButton)close).Glyph = "\uE711";
            close.Location = new Point(ClientSize.Width - 42, 7);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.AccessibleName = "Close dialog";
            close.Click += delegate { Close(); };
            titleBar.Controls.Add(close);
            titleBar.Resize += delegate { close.Location = new Point(Math.Max(0, titleBar.ClientSize.Width - 40), 7); };
            Controls.Add(titleBar);

            ContentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ModernTheme.Background
            };
            Controls.Add(ContentPanel);
            ContentPanel.BringToFront();
        }

        protected static Button RetroButton(string text, int width, int height)
        {
            return new RetroDialogButton
            {
                Text = text,
                Size = new Size(width, Math.Max(ModernTheme.CompactHeight, height)),
                Font = ModernTheme.Font(9, FontStyle.Bold),
                Primary = text == "Save" || text == "Add selected" || text == "Stop alarm",
                BackColor = text == "Save" || text == "Add selected" || text == "Stop alarm" ? ModernTheme.Blue : ModernTheme.Surface,
                ForeColor = text == "Save" || text == "Add selected" || text == "Stop alarm" ? Color.White : ModernTheme.Text
            };
        }

        protected override void OnShown(EventArgs e)
        {
            ApplyModernStyle(ContentPanel);
            base.OnShown(e);
        }

        private static void ApplyModernStyle(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                // New components already carry semantic colors and icon fonts.
                if (control is ModernCard || control is ModernIcon || control is StatusBadge || control is ModernButton) continue;
                if (!(control is RetroDialogButton))
                {
                    if (control.ForeColor == SystemColors.ControlText || control.ForeColor == Color.Black || control.ForeColor == Color.Navy) control.ForeColor = ModernTheme.Text;
                    if (!(control is PixelGerberaPanel)) control.BackColor = control is TextBox || control is ListView || control is TabPage || control is Panel ? ModernTheme.Surface : ModernTheme.Background;
                    if (control.Font.Name != "Segoe MDL2 Assets") control.Font = ModernTheme.Font(control.Font.Size, control.Font.Style);
                }
                Label label = control as Label;
                if (label != null)
                {
                    label.BorderStyle = BorderStyle.None;
                    label.BackColor = Color.Transparent;
                    if (label.Text == "!") { label.BackColor = ModernTheme.Warning; label.ForeColor = ModernTheme.Text; }
                }
                Panel panel = control as Panel;
                if (panel != null) panel.BorderStyle = BorderStyle.None;
                TextBox box = control as TextBox;
                if (box != null) box.BorderStyle = BorderStyle.FixedSingle;
                ListView list = control as ListView;
                if (list != null) { list.BorderStyle = BorderStyle.FixedSingle; list.GridLines = false; }
                ApplyModernStyle(control);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath shape = ModernTheme.Rounded(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), ModernTheme.Radius))
            using (Pen pen = new Pen(ModernTheme.Border)) e.Graphics.DrawPath(pen, shape);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width < 2 || Height < 2) return;
            using (System.Drawing.Drawing2D.GraphicsPath shape = ModernTheme.Rounded(new Rectangle(0, 0, Width, Height), ModernTheme.Radius))
            {
                Region previous = Region;
                Region = new Region(shape);
                if (previous != null) previous.Dispose();
            }
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

        private readonly bool playSound;
        private readonly string customAlarmPath;
        private System.Media.SoundPlayer customSoundPlayer;

        public TimerAlertForm(string alertType, string finishedMinutes, string nextMinutes, bool previewOnly = false, bool playSound = true)
            : this(alertType, finishedMinutes, nextMinutes, previewOnly, playSound, null) { }

        public TimerAlertForm(string alertType, string finishedMinutes, string nextMinutes, bool previewOnly, bool playSound, string customAlarmPath)
        {
            this.previewOnly = previewOnly;
            this.playSound = playSound;
            this.customAlarmPath = customAlarmPath ?? "";
            bool breakFinished = string.Equals(alertType, "break-finished", StringComparison.OrdinalIgnoreCase);
            bool workAlert = alertType.StartsWith("work-", StringComparison.OrdinalIgnoreCase);
            breakFinished = breakFinished || string.Equals(alertType, "work-break-finished", StringComparison.OrdinalIgnoreCase);
            string heading = breakFinished ? "Break over!" : "Time for a break!";
            string timerName = breakFinished ? (workAlert ? "Work timer" : "Game timer") : "Break timer";
            string message = breakFinished ? (workAlert ? "You can work again." : "You can play again.") : (workAlert ? "Your work time is up." : "Your game time is up.");

            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 272);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            BuildRetroChrome("Game Management Alarm");

            Color accent = workAlert ? ModernTheme.Work : ModernTheme.Game;
            ModernCard messagePanel = new ModernCard { Location = new Point(16, 16), Size = new Size(506, 156) };
            ModernIcon warningIcon = new ModernIcon { Glyph = breakFinished ? "check" : "clock", Circle = true,
                Accent = breakFinished ? ModernTheme.Success : accent, Location = new Point(20, 20), Size = new Size(44, 44) };
            messagePanel.Controls.Add(warningIcon);
            Label headingLabel = ModernTheme.Label(heading, 17, true);
            headingLabel.AutoSize = false;
            headingLabel.SetBounds(80, 20, 404, 32);
            messagePanel.Controls.Add(headingLabel);
            Label messageLabel = ModernTheme.Label(message + Environment.NewLine + timerName + ": " + FormatDuration(nextMinutes) + ".", 10);
            messageLabel.AutoSize = false;
            messageLabel.ForeColor = ModernTheme.Secondary;
            messageLabel.SetBounds(80, 60, 404, 55);
            messagePanel.Controls.Add(messageLabel);
            Label completedLabel = ModernTheme.Label("Completed " + FormatDuration(finishedMinutes), 8.5F);
            completedLabel.ForeColor = ModernTheme.Secondary;
            completedLabel.Location = new Point(80, 124);
            messagePanel.Controls.Add(completedLabel);

            Button stopButton = RetroButton("Stop alarm", 128, ModernTheme.ControlHeight);
            ((ModernButton)stopButton).Accent = accent;
            stopButton.Location = new Point(394, 188);
            stopButton.AccessibleName = "Stop alarm";
            stopButton.Click += delegate { Close(); };
            Label status = ModernTheme.Label(playSound ? (CanUseCustomAlarm(this.customAlarmPath) ? "Custom alarm is sounding" : "Windows alarm is sounding") : "Sound is turned off", 9);
            status.ForeColor = ModernTheme.Secondary;
            status.Location = new Point(20, 198);
            ContentPanel.Controls.Add(messagePanel);
            ContentPanel.Controls.Add(status);
            ContentPanel.Controls.Add(stopButton);
            AcceptButton = stopButton;
            CancelButton = stopButton;
            soundTimer.Interval = 1500;
            soundTimer.Tick += delegate { PlayAlarmSound(); };
            Shown += delegate
            {
                if (!this.previewOnly && this.playSound)
                {
                    if (!TryStartCustomAlarm())
                    {
                        status.Text = String.IsNullOrWhiteSpace(this.customAlarmPath) ? "Windows alarm is sounding" : "Custom sound unavailable; using Windows alarm";
                        PlayAlarmSound();
                        soundTimer.Start();
                    }
                }
                Activate();
                ActiveControl = null;
            };
            FormClosed += delegate
            {
                soundTimer.Stop();
                soundTimer.Dispose();
                if (customSoundPlayer != null) { try { customSoundPlayer.Stop(); customSoundPlayer.Dispose(); } catch { } customSoundPlayer = null; }
            };
        }

        private bool TryStartCustomAlarm()
        {
            if (!CanUseCustomAlarm(customAlarmPath)) return false;
            try
            {
                customSoundPlayer = new System.Media.SoundPlayer(customAlarmPath);
                customSoundPlayer.Load();
                customSoundPlayer.PlayLooping();
                return true;
            }
            catch
            {
                if (customSoundPlayer != null) { try { customSoundPlayer.Dispose(); } catch { } customSoundPlayer = null; }
                return false;
            }
        }

        private static bool CanUseCustomAlarm(string path)
        {
            try { return !String.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && String.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase) && File.Exists(path) && new FileInfo(path).Length <= 25L * 1024L * 1024L; }
            catch { return false; }
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

            int totalSeconds = (int)Math.Round(minutes * 60, MidpointRounding.AwayFromZero);
            List<string> parts = new List<string>();
            int hours = totalSeconds / 3600, wholeMinutes = totalSeconds / 60 % 60, secondsLeft = totalSeconds % 60;
            if (hours > 0) parts.Add(hours + (hours == 1 ? " hour" : " hours"));
            if (wholeMinutes > 0) parts.Add(wholeMinutes + (wholeMinutes == 1 ? " minute" : " minutes"));
            if (secondsLeft > 0 || parts.Count == 0) parts.Add(secondsLeft + (secondsLeft == 1 ? " second" : " seconds"));
            return string.Join(" ", parts.ToArray());
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

        internal static void PlayOneAlarmSound(string customAlarmPath = null)
        {
            if (CanUseCustomAlarm(customAlarmPath))
            {
                try { using (System.Media.SoundPlayer player = new System.Media.SoundPlayer(customAlarmPath)) { player.Load(); player.PlaySync(); } return; }
                catch { }
            }
            PlayAlarmSound();
        }
    }

    internal sealed class GameSettings
    {
        public int pollSeconds { get; set; }
        public int gameBrightnessPercent { get; set; }
        public bool gameTimerEnabled { get; set; }
        public int gameTimerMinutes { get; set; }
        public int? gameTimerSeconds { get; set; }
        public int? breakTimerSeconds { get; set; }
        public int? workTimerSeconds { get; set; }
        public int? workBreakSeconds { get; set; }
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
        public bool timerPopupEnabled { get; set; }
        public bool timerSoundEnabled { get; set; }
        public string customAlarmPath { get; set; }
        public bool sessionSummaryEnabled { get; set; }
        public bool automaticModeDetection { get; set; }
    }

    // One canonical unit throughout persistence and countdowns; legacy minutes migrate on save.
    internal static class TimerDuration
    {
        internal static int Resolve(int? seconds, int minutes, int fallbackMinutes)
        {
            long value = seconds.HasValue ? seconds.Value : (long)(minutes > 0 ? minutes : fallbackMinutes) * 60;
            return (int)Math.Max(1, Math.Min(86400, value));
        }

        internal static string Format(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return seconds >= 3600 ? string.Format("{0:00}:{1:00}:{2:00}", seconds / 3600, seconds / 60 % 60, seconds % 60)
                : string.Format("{0:00}:{1:00}", seconds / 60, seconds % 60);
        }
    }

    internal sealed class DurationInput : UserControl
    {
        private readonly NumericUpDown hours = new NumericUpDown();
        private readonly NumericUpDown minutes = new NumericUpDown();
        private readonly NumericUpDown seconds = new NumericUpDown();
        public event EventHandler ValueChanged;
        public DurationInput()
        {
            Size = new Size(240, ModernTheme.ControlHeight);
            Font = ModernTheme.Font(10);
            NumericUpDown[] fields = { hours, minutes, seconds };
            string[] units = { "h", "m", "s" };
            for (int i = 0; i < fields.Length; i++)
            {
                NumericUpDown field = fields[i];
                field.SetBounds(i * 80, 6, 58, 28);
                field.Minimum = 0;
                field.Maximum = i == 0 ? 24 : 59;
                field.TextAlign = HorizontalAlignment.Right;
                field.AccessibleName = new[] { "Hours", "Minutes", "Seconds" }[i];
                field.ValueChanged += delegate
                {
                    if (hours.Value == 24) { minutes.Value = 0; seconds.Value = 0; }
                    minutes.Enabled = seconds.Enabled = hours.Value < 24;
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                };
                Controls.Add(field);
                Controls.Add(new Label { Text = units[i], Location = new Point(i * 80 + 61, 10), AutoSize = true });
            }
        }
        public int TotalSeconds
        {
            get { return Math.Min(86400, (int)(hours.Value * 3600 + minutes.Value * 60 + seconds.Value)); }
            set
            {
                int duration = Math.Max(0, Math.Min(86400, value));
                hours.Value = duration / 3600;
                minutes.Value = duration / 60 % 60;
                seconds.Value = duration % 60;
            }
        }
    }

    internal sealed class SessionClock
    {
        internal double ElapsedSeconds;
        internal DateTime? RunningSince;
        internal bool Paused;
        internal TimeSpan ElapsedAt(DateTime now)
        {
            double delta = !Paused && RunningSince.HasValue ? Math.Max(0, (now - RunningSince.Value).TotalSeconds) : 0;
            return TimeSpan.FromSeconds(Math.Max(0, ElapsedSeconds + delta));
        }
        internal static SessionClock Load(string root, string mode, JavaScriptSerializer serializer)
        {
            foreach (string name in new[] { "runtime-state.json", mode + "-session-state.json" })
            {
                try
                {
                    string path = Path.Combine(root, name);
                    if (!File.Exists(path)) continue;
                    Dictionary<string, object> state = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                    object item;
                    if (!state.TryGetValue("Mode", out item) || !string.Equals(Convert.ToString(item), mode, StringComparison.OrdinalIgnoreCase)) continue;
                    // Pause commits the frozen checkpoint before deleting runtime. Honor that commit
                    // even if runtime cleanup was interrupted, but never mix different sessions.
                    if (name == "runtime-state.json")
                    {
                        string savedPath = Path.Combine(root, mode + "-session-state.json");
                        if (File.Exists(savedPath))
                        {
                            try
                            {
                                var saved = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(savedPath));
                                object savedId, runtimeId, paused, savedMode;
                                if (saved.TryGetValue("Mode", out savedMode) && string.Equals(Convert.ToString(savedMode), mode, StringComparison.OrdinalIgnoreCase) &&
                                    saved.TryGetValue("Paused", out paused) && Convert.ToBoolean(paused) &&
                                    saved.TryGetValue("SessionId", out savedId) && state.TryGetValue("SessionId", out runtimeId) &&
                                    !string.IsNullOrWhiteSpace(Convert.ToString(savedId)) && string.Equals(Convert.ToString(savedId), Convert.ToString(runtimeId), StringComparison.Ordinal)) state = saved;
                            }
                            catch { }
                        }
                    }
                    SessionClock clock = new SessionClock();
                    if (state.TryGetValue("SessionElapsedSeconds", out item) || state.TryGetValue("ElapsedSeconds", out item)) clock.ElapsedSeconds = Convert.ToDouble(item);
                    clock.Paused = state.TryGetValue("Paused", out item) && Convert.ToBoolean(item);
                    DateTime began;
                    if ((state.TryGetValue("SessionRunningSince", out item) || state.TryGetValue("RunningSince", out item) || state.TryGetValue("ManagementStarted", out item)) &&
                        DateTime.TryParse(Convert.ToString(item), null, System.Globalization.DateTimeStyles.RoundtripKind, out began)) clock.RunningSince = began.ToLocalTime();
                    return clock;
                }
                catch { }
            }
            return new SessionClock();
        }
    }

    internal static class ForegroundModeDetector
    {
        internal static string Classify(string executablePath, IEnumerable<string> gameApps,
            IEnumerable<string> workApps, IEnumerable<string> gameFolders)
        {
            string path = Normalize(executablePath);
            if (path == null) return null;
            bool game = (gameApps ?? Enumerable.Empty<string>()).Any(item =>
                string.Equals(Normalize(item), path, StringComparison.OrdinalIgnoreCase));
            bool work = (workApps ?? Enumerable.Empty<string>()).Any(item =>
                string.Equals(Normalize(item), path, StringComparison.OrdinalIgnoreCase));
            if (!game)
            {
                game = (gameFolders ?? Enumerable.Empty<string>()).Any(folder => IsInside(path, Normalize(folder)));
            }
            if (game == work) return null;
            return game ? "game" : "work";
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return null; }
        }

        private static bool IsInside(string path, string folder)
        {
            if (path == null || folder == null) return false;
            return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class TimerResetForm : RetroDialogForm
    {
        internal string SelectedAction { get; private set; }

        internal TimerResetForm(string mode)
        {
            ClientSize = new Size(390, 205);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BuildRetroChrome("Reset " + (mode == "work" ? "Work" : "Game") + " Timer");
            Label explanation = new Label
            {
                Text = "Choose what you want to reset for this mode only.",
                Location = new Point(18, 17), AutoSize = true
            };
            ContentPanel.Controls.Add(explanation);
            AddActionButton("Reset timer", "reset-timer", 18, 52);
            AddActionButton("Reset cycles", "reset-cycles", 142, 52);
            AddActionButton("Reset all", "reset-all", 266, 52);
            Button cancel = RetroButton("Cancel", 92, 25);
            cancel.Location = new Point(280, 118);
            cancel.DialogResult = DialogResult.Cancel;
            ContentPanel.Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void AddActionButton(string text, string action, int x, int y)
        {
            Button button = RetroButton(text, 106, 28);
            button.Location = new Point(x, y);
            button.Click += delegate { SelectedAction = action; DialogResult = DialogResult.OK; Close(); };
            ContentPanel.Controls.Add(button);
        }
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
            ClientSize = new Size(560, 442);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            TopMost = true;
            BuildRetroChrome("Game Management - Session saved");

            bool workSession = string.Equals(summary.Mode, "work", StringComparison.OrdinalIgnoreCase);
            ModernIcon icon = new ModernIcon { Glyph = "check", Accent = ModernTheme.Success, Circle = true, Size = new Size(44, 44), Location = new Point(20, 16) };
            ContentPanel.Controls.Add(icon);
            Label heading = ModernTheme.Label(workSession ? "Work session complete" : "Game session complete", 17, true);
            heading.SetBounds(80, 18, 454, 32);
            heading.AutoSize = false;
            ContentPanel.Controls.Add(heading);
            Label subheading = ModernTheme.Label("Your recorded session is ready to review.", 9);
            subheading.ForeColor = ModernTheme.Secondary;
            subheading.Location = new Point(80, 50);
            ContentPanel.Controls.Add(subheading);

            ModernCard detailsGroup = new ModernCard { Location = new Point(16, 84), Size = new Size(526, 276) };
            Label detailsHeading = ModernTheme.Label("Session summary", 10, true);
            detailsHeading.Location = new Point(16, 16);
            detailsGroup.Controls.Add(detailsHeading);
            string[] names = { workSession ? "Applications" : "Game", workSession ? "Worked" : "Played", "Timer cycles", "Peak CPU temp", "Peak GPU temp", "Started", "Finished" };
            string[] values = { Safe(summary.Game), Safe(summary.DurationText), summary.Cycles.ToString(), Safe(summary.PeakCpu), Safe(summary.PeakGpu), Safe(summary.StartedAt), Safe(summary.EndedAt) };
            TableLayoutPanel details = new TableLayoutPanel { Location = new Point(16, 48), Size = new Size(494, 212), ColumnCount = 2, RowCount = names.Length, BackColor = ModernTheme.Surface };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < names.Length; i++)
            {
                details.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / names.Length));
                Label key = ModernTheme.Label(names[i], 9);
                key.AutoSize = false; key.Dock = DockStyle.Fill; key.TextAlign = ContentAlignment.MiddleLeft; key.ForeColor = ModernTheme.Secondary; key.Margin = Padding.Empty;
                Label value = ModernTheme.Label(values[i], 9, i == 1);
                value.AutoSize = false; value.AutoEllipsis = true; value.Dock = DockStyle.Fill; value.TextAlign = ContentAlignment.MiddleLeft; value.Margin = Padding.Empty; value.AccessibleName = names[i] + ": " + values[i];
                details.Controls.Add(key, 0, i); details.Controls.Add(value, 1, i);
            }
            detailsGroup.Controls.Add(details);
            ContentPanel.Controls.Add(detailsGroup);
            Label savedLabel = ModernTheme.Label("Saved in GameSessionHistory.txt", 8.5F);
            savedLabel.Location = new Point(20, 386);
            savedLabel.ForeColor = ModernTheme.Secondary;
            ContentPanel.Controls.Add(savedLabel);
            Button closeButton = RetroButton("Close", 104, ModernTheme.ControlHeight);
            ((ModernButton)closeButton).Primary = true;
            closeButton.Location = new Point(438, 376);
            closeButton.AccessibleName = "Close session summary";
            closeButton.Click += delegate { Close(); };
            closeButton.DialogResult = DialogResult.Cancel;
            ContentPanel.Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;
        }

        private static string Safe(string value) { return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value; }
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
            list.Items.Add(new ListViewItem("Loading installed applications...") { ForeColor = ModernTheme.Secondary });
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
        public string SessionId;
        public string Mode;
        public string Application;
        public DateTime Start;
        public DateTime End;
        public int Cycles;
        public double? ActiveSeconds;
        public List<SessionInterval> ActiveSegments;
        internal TimeSpan Duration { get { return ActiveSeconds.HasValue ? TimeSpan.FromSeconds(Math.Max(0, ActiveSeconds.Value)) : End - Start; } }
    }

    internal sealed class SessionInterval
    {
        public DateTime Start;
        public DateTime End;
    }

    internal static class WeeklyReport
    {
        internal static List<TrackedSession> Load(string path)
        {
            List<TrackedSession> sessions = new List<TrackedSession>();
            if (!File.Exists(path)) return sessions;
            string sessionId = "", mode = "game", application = "";
            DateTime start = DateTime.MinValue, end = DateTime.MinValue;
            int cycles = 0;
            double? activeSeconds = null;
            List<SessionInterval> activeSegments = null;
            Action flush = delegate
            {
                if (start != DateTime.MinValue && end > start)
                    sessions.Add(new TrackedSession { SessionId = sessionId, Mode = mode, Application = application, Start = start, End = end, Cycles = cycles, ActiveSeconds = activeSeconds, ActiveSegments = activeSegments });
                sessionId = ""; mode = "game"; application = ""; start = end = DateTime.MinValue; cycles = 0;
                activeSeconds = null; activeSegments = null;
            };
            foreach (string line in File.ReadLines(path))
            {
                if (line.StartsWith("----", StringComparison.Ordinal)) { flush(); continue; }
                int separator = line.IndexOf(':');
                if (separator < 0) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key == "SessionId") sessionId = value;
                else if (key == "Mode") mode = string.Equals(value, "work", StringComparison.OrdinalIgnoreCase) ? "work" : "game";
                else if (key == "Applications" || key == "Game") application = value;
                else if (key == "Started") start = ParseHistoryTime(value);
                else if (key == "Finished") end = ParseHistoryTime(value);
                else if (key == "Timer cycles") int.TryParse(value, out cycles);
                else if (key == "ActiveSeconds")
                {
                    double duration;
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out duration) && !double.IsNaN(duration) && !double.IsInfinity(duration) && duration >= 0) activeSeconds = duration;
                }
                else if (key == "ActiveSegments")
                {
                    try
                    {
                        Dictionary<string, object>[] intervals = new JavaScriptSerializer().Deserialize<Dictionary<string, object>[]>(value);
                        activeSegments = new List<SessionInterval>();
                        foreach (var interval in intervals)
                        {
                            if (interval == null || !interval.ContainsKey("StartedAt") || !interval.ContainsKey("EndedAt")) continue;
                            DateTime a, b;
                            if (DateTime.TryParse(Convert.ToString(interval["StartedAt"]), null, System.Globalization.DateTimeStyles.RoundtripKind, out a) &&
                                DateTime.TryParse(Convert.ToString(interval["EndedAt"]), null, System.Globalization.DateTimeStyles.RoundtripKind, out b) && b > a)
                                activeSegments.Add(new SessionInterval { Start = a.Kind == DateTimeKind.Unspecified ? a : a.ToLocalTime(), End = b.Kind == DateTimeKind.Unspecified ? b : b.ToLocalTime() });
                        }
                        if (activeSegments.Count == 0) activeSegments = null;
                    }
                    catch { activeSegments = null; }
                }
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
            return BuildPeriod(sessions, weekStart.Date, weekStart.Date.AddDays(7), "WEEKLY SUMMARY", "Week of ");
        }

        internal static string BuildMonthly(IEnumerable<TrackedSession> sessions, DateTime monthStart)
        {
            DateTime beginning = new DateTime(monthStart.Year, monthStart.Month, 1);
            return BuildPeriod(sessions, beginning, beginning.AddMonths(1), "MONTHLY SUMMARY", "Month of ");
        }

        private static string BuildPeriod(IEnumerable<TrackedSession> sessions, DateTime beginning, DateTime ending, string summaryTitle, string periodLabel)
        {
            int dayCount = (int)(ending - beginning).TotalDays;
            TimeSpan[] game = new TimeSpan[dayCount], work = new TimeSpan[dayCount];
            Dictionary<string, TimeSpan> gameApps = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TimeSpan> workApps = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
            int gameSessions = 0, workSessions = 0;
            int unattributed = 0;
            foreach (TrackedSession session in sessions)
            {
                if (session == null || session.End <= session.Start || session.End <= beginning || session.Start >= ending) continue;
                bool isWork = string.Equals(session.Mode, "work", StringComparison.OrdinalIgnoreCase);
                var intervals = session.ActiveSegments ?? new List<SessionInterval> { new SessionInterval { Start = session.Start, End = session.End } };
                double scale = session.ActiveSegments == null && session.ActiveSeconds.HasValue
                    ? Math.Min(1, session.ActiveSeconds.Value / (session.End - session.Start).TotalSeconds) : 1;
                TimeSpan weeklyDuration = TimeSpan.Zero;
                foreach (SessionInterval interval in intervals)
                {
                    DateTime cursor = interval.Start > beginning ? interval.Start : beginning;
                    DateTime stop = interval.End < ending ? interval.End : ending;
                    if (stop <= cursor) continue;
                    weeklyDuration += TimeSpan.FromTicks((long)((stop - cursor).Ticks * scale));
                    while (cursor < stop)
                    {
                        int day = (int)(cursor.Date - beginning).TotalDays;
                        DateTime dayEnd = cursor.Date.AddDays(1);
                        DateTime sliceEnd = stop < dayEnd ? stop : dayEnd;
                        TimeSpan slice = TimeSpan.FromTicks((long)((sliceEnd - cursor).Ticks * scale));
                        if (isWork) work[day] += slice; else game[day] += slice;
                        cursor = sliceEnd;
                    }
                }
                if (weeklyDuration <= TimeSpan.Zero) continue;
                if (isWork) workSessions++; else gameSessions++;
                if (string.IsNullOrWhiteSpace(session.Application) || session.Application.Contains(",")) unattributed++;
                else
                {
                    Dictionary<string, TimeSpan> apps = isWork ? workApps : gameApps;
                    TimeSpan prior;
                    apps.TryGetValue(session.Application, out prior);
                    apps[session.Application] = prior + weeklyDuration;
                }
            }
            StringBuilder report = new StringBuilder();
            report.AppendLine(periodLabel + beginning.ToString("yyyy-MM-dd") + " to " + ending.AddDays(-1).ToString("yyyy-MM-dd"));
            report.AppendLine();
            for (int day = 0; day < dayCount; day++)
                report.AppendLine(beginning.AddDays(day).ToString("dddd, MMM d") + "    Game " + Format(game[day]) + "    Work " + Format(work[day]) + "    Total " + Format(game[day] + work[day]));
            TimeSpan gameTotal = TimeSpan.FromTicks(game.Sum(item => item.Ticks));
            TimeSpan workTotal = TimeSpan.FromTicks(work.Sum(item => item.Ticks));
            report.AppendLine().AppendLine(summaryTitle);
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
        private readonly string historyPath;
        private readonly List<TrackedSession> sessions;
        private readonly TextBox reportText;
        private readonly ComboBox periodChoice;
        private readonly Label periodLabel;
        private readonly TextBox searchBox;
        private readonly ListView historyList;
        private DateTime periodStart;

        public WeeklyReportForm(string historyPath)
        {
            this.historyPath = historyPath;
            ClientSize = new Size(720, 590);
            StartPosition = FormStartPosition.CenterParent;
            BuildRetroChrome("Reports and Session History");
            try { sessions = WeeklyReport.Load(historyPath); }
            catch { sessions = new List<TrackedSession>(); }

            TabControl tabs = new TabControl { Location = new Point(14, 12), Size = new Size(692, 500) };
            TabPage reportPage = new TabPage("Reports") { BackColor = ModernTheme.Surface };
            TabPage historyPage = new TabPage("Session history") { BackColor = ModernTheme.Surface };
            tabs.TabPages.Add(reportPage);
            tabs.TabPages.Add(historyPage);
            ContentPanel.Controls.Add(tabs);

            periodChoice = new ComboBox { Location = new Point(12, 13), Size = new Size(105, 21), DropDownStyle = ComboBoxStyle.DropDownList };
            periodChoice.Items.AddRange(new object[] { "Weekly", "Monthly" });
            periodChoice.SelectedIndex = 0;
            reportPage.Controls.Add(periodChoice);

            Button previous = RetroButton("< Previous", 92, 25);
            previous.Location = new Point(128, 11);
            previous.Click += delegate { MovePeriod(-1); };
            reportPage.Controls.Add(previous);
            Button next = RetroButton("Next >", 92, 25);
            next.Location = new Point(228, 11);
            next.Click += delegate { MovePeriod(1); };
            reportPage.Controls.Add(next);
            periodLabel = new Label { Location = new Point(332, 16), Size = new Size(325, 18), TextAlign = ContentAlignment.MiddleRight };
            reportPage.Controls.Add(periodLabel);

            reportText = new TextBox { Location = new Point(12, 45), Size = new Size(653, 414), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = ModernTheme.Font(9F), BackColor = ModernTheme.Surface };
            reportPage.Controls.Add(reportText);

            historyPage.Controls.Add(new Label { Text = "Search:", Location = new Point(12, 17), AutoSize = true });
            searchBox = new TextBox { Location = new Point(65, 13), Size = new Size(600, 21) };
            searchBox.TextChanged += delegate { RefreshHistory(); };
            historyPage.Controls.Add(searchBox);
            historyList = new ListView { Location = new Point(12, 45), Size = new Size(653, 414), View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false };
            historyList.Columns.Add("Date", 130);
            historyList.Columns.Add("Mode", 65);
            historyList.Columns.Add("Application", 275);
            historyList.Columns.Add("Time", 90);
            historyList.Columns.Add("Cycles", 60);
            historyPage.Controls.Add(historyList);

            DateTime today = DateTime.Today;
            periodStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            periodChoice.SelectedIndexChanged += delegate
            {
                periodStart = periodChoice.SelectedIndex == 1
                    ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                    : DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
                RefreshReport();
            };
            RefreshReport();
            RefreshHistory();

            Button close = RetroButton("Close", 92, 25);
            close.Location = new Point(614, 520);
            close.Click += delegate { Close(); };
            ContentPanel.Controls.Add(close);
        }

        private void MovePeriod(int direction)
        {
            periodStart = periodChoice.SelectedIndex == 1 ? periodStart.AddMonths(direction) : periodStart.AddDays(direction * 7);
            RefreshReport();
        }

        private void RefreshReport()
        {
            try
            {
                if (periodChoice.SelectedIndex == 1)
                {
                    periodStart = new DateTime(periodStart.Year, periodStart.Month, 1);
                    periodLabel.Text = periodStart.ToString("MMMM yyyy");
                    reportText.Text = WeeklyReport.BuildMonthly(sessions, periodStart);
                }
                else
                {
                    periodStart = periodStart.Date.AddDays(-(((int)periodStart.DayOfWeek + 6) % 7));
                    periodLabel.Text = periodStart.ToString("MMM d") + " - " + periodStart.AddDays(6).ToString("MMM d, yyyy");
                    reportText.Text = WeeklyReport.Build(sessions, periodStart);
                }
            }
            catch (Exception ex) { reportText.Text = "The report could not be read: " + ex.Message; }
        }

        private void RefreshHistory()
        {
            string query = searchBox.Text.Trim();
            historyList.BeginUpdate();
            historyList.Items.Clear();
            foreach (TrackedSession session in sessions.OrderByDescending(item => item.Start))
            {
                string duration = FormatHistoryDuration(session.Duration);
                string searchable = session.Start.ToString("yyyy-MM-dd HH:mm") + " " + session.Mode + " " + session.Application + " " + duration;
                if (query.Length > 0 && searchable.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                ListViewItem item = new ListViewItem(session.Start.ToString("yyyy-MM-dd HH:mm"));
                item.SubItems.Add(string.Equals(session.Mode, "work", StringComparison.OrdinalIgnoreCase) ? "Work" : "Game");
                item.SubItems.Add(string.IsNullOrWhiteSpace(session.Application) ? "Unknown" : session.Application);
                item.SubItems.Add(duration);
                item.SubItems.Add(session.Cycles.ToString());
                historyList.Items.Add(item);
            }
            historyList.EndUpdate();
        }

        private static string FormatHistoryDuration(TimeSpan duration)
        {
            return ((int)duration.TotalHours) + "h " + duration.Minutes.ToString("00") + "m";
        }
    }

    internal sealed class DataOptionsForm : RetroDialogForm
    {
        private readonly CheckBox popupCheck;
        private readonly CheckBox soundCheck;
        private readonly CheckBox summaryCheck;

        internal bool TimerPopupEnabled { get { return popupCheck.Checked; } }
        internal bool TimerSoundEnabled { get { return soundCheck.Checked; } }
        internal bool SessionSummaryEnabled { get { return summaryCheck.Checked; } }

        internal DataOptionsForm(bool popupEnabled, bool soundEnabled, bool summaryEnabled, Action backup, Action restore)
        {
            ClientSize = new Size(470, 315);
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BuildRetroChrome("Game Management Options");

            GroupBox notifications = new GroupBox { Text = "Notifications", Location = new Point(18, 15), Size = new Size(434, 132) };
            popupCheck = new CheckBox { Text = "Show timer popup", Location = new Point(17, 26), AutoSize = true, Checked = popupEnabled };
            soundCheck = new CheckBox { Text = "Play timer alarm sound", Location = new Point(17, 55), AutoSize = true, Checked = soundEnabled };
            summaryCheck = new CheckBox { Text = "Show session summary after an app closes", Location = new Point(17, 84), AutoSize = true, Checked = summaryEnabled };
            notifications.Controls.Add(popupCheck);
            notifications.Controls.Add(soundCheck);
            notifications.Controls.Add(summaryCheck);
            ContentPanel.Controls.Add(notifications);

            GroupBox data = new GroupBox { Text = "Portable backup", Location = new Point(18, 157), Size = new Size(434, 75) };
            Button backupButton = RetroButton("Backup data...", 126, 25);
            backupButton.Location = new Point(17, 29);
            backupButton.Click += delegate { if (backup != null) backup(); };
            data.Controls.Add(backupButton);
            Button restoreButton = RetroButton("Restore data...", 126, 25);
            restoreButton.Location = new Point(154, 29);
            restoreButton.Click += delegate { if (restore != null) restore(); };
            data.Controls.Add(restoreButton);
            Label dataLabel = new Label { Text = "Settings + history", Location = new Point(293, 34), AutoSize = true };
            data.Controls.Add(dataLabel);
            ContentPanel.Controls.Add(data);

            Button save = RetroButton("Save", 92, 25);
            save.Location = new Point(260, 244);
            save.DialogResult = DialogResult.OK;
            ContentPanel.Controls.Add(save);
            Button close = RetroButton("Cancel", 92, 25);
            close.Location = new Point(360, 244);
            close.DialogResult = DialogResult.Cancel;
            ContentPanel.Controls.Add(close);
            AcceptButton = save;
            CancelButton = close;
        }
    }

    internal sealed partial class MainForm : Form
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private readonly string rootPath;
        private readonly string settingsPath;
        private readonly string logPath;
        private readonly string statePath;
        private readonly string timerStatePath;
        private readonly string workTimerStatePath;
        private readonly string timerCountersPath;
        private readonly string timerResetRequestPath;
        private readonly string timerResetAckPath;
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
        private readonly Timer foregroundModeTimer = new Timer();
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
        private bool timerPopupEnabled = true;
        private bool timerSoundEnabled = true;
        private string customAlarmPath = "";
        private bool sessionSummaryEnabled = true;
        private string lastAssignedForegroundPath;
        private string manualOverridePath;
        private DateTime lastAutomaticModeSwitch = DateTime.MinValue;

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
        private DurationInput timerMinutesInput;
        private DurationInput breakMinutesInput;
        private CheckBox timerEnabledCheck;
        private CheckBox pauseWithEscapeCheck;
        private CheckBox automaticModeCheck;
        private CheckBox workAutomaticModeCheck;
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
        private DurationInput workTimerMinutesInput;
        private DurationInput workBreakMinutesInput;
        private SessionClock workSessionClock = new SessionClock();
        private DateTime? workSessionStartedAt;
        private bool workView;
        private bool targetWorkView;
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
            rootPath = PreviewMode ? (PreviewRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-data")) : File.Exists(Path.Combine(documentsRoot, "GameManagement.ps1"))
                ? documentsRoot
                : AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            settingsPath = Path.Combine(rootPath, "settings.json");
            logPath = Path.Combine(rootPath, "GameManagement.log");
            statePath = Path.Combine(rootPath, "runtime-state.json");
            timerStatePath = Path.Combine(rootPath, "timer-state.json");
            workTimerStatePath = Path.Combine(rootPath, "work-timer-state.json");
            timerCountersPath = Path.Combine(rootPath, "timer-counters.json");
            timerResetRequestPath = Path.Combine(rootPath, "timer-reset-request.json");
            timerResetAckPath = Path.Combine(rootPath, "timer-reset-ack.json");
            historyPath = Path.Combine(rootPath, "GameSessionHistory.txt");
            performanceStatePath = Path.Combine(rootPath, "performance-state.json");
            engineEnabledPath = Path.Combine(rootPath, "engine-enabled.flag");
            watcherPath = Path.Combine(rootPath, "GameManagement.ps1");
            installerPath = Path.Combine(rootPath, "Install-GameManagement.ps1");
            pausePath = Path.Combine(rootPath, "Pause-GameManagement.ps1");

            BuildWindow();
            BuildTrayIcon();
            if (!PreviewMode) InitializeUiSignals();
            LoadSettings();
            SyncOptions();
            StartModernViewRefresh();
            if (PreviewMode)
            {
                trayIcon.Visible = false;
                RefreshTimerDisplay(false);
                RefreshWorkTimerDisplay();
                RefreshModernView();
                return;
            }
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

            foregroundModeTimer.Interval = 1000;
            foregroundModeTimer.Tick += delegate { CheckForegroundMode(); };
            foregroundModeTimer.Start();

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

        private void AutomaticModeSettingChanged(CheckBox source)
        {
            if (source == null) return;
            bool enabled = source.Checked;
            if (automaticModeCheck != null && automaticModeCheck != source) automaticModeCheck.Checked = enabled;
            if (workAutomaticModeCheck != null && workAutomaticModeCheck != source) workAutomaticModeCheck.Checked = enabled;
            if (!source.Focused || settings == null) return;
            if (!enabled)
            {
                manualOverridePath = null;
                lastAssignedForegroundPath = null;
            }
            SaveSettingsAsync(null, delegate(bool saved)
            {
                if (saved) SetFooter(enabled ? "Automatic mode switching enabled." : "Automatic mode switching disabled.");
            });
        }

        private string GetForegroundExecutablePath()
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return null;
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                if (processId == 0) return null;
                using (Process process = Process.GetProcessById((int)processId))
                    return process.MainModule.FileName;
            }
            catch { return null; }
        }

        private void CheckForegroundMode()
        {
            if (PreviewMode || settings == null || !settings.automaticModeDetection || HasUnsavedSettings() || exiting || modeSwitchInProgress ||
                settingsSaveInProgress || IsWatcherTransitionActive()) return;
            string foregroundPath = GetForegroundExecutablePath();
            string targetMode = ForegroundModeDetector.Classify(foregroundPath, settings.gameApps, settings.workApps, settings.gameFolders);
            if (targetMode == null) return;

            bool changedApp = !string.Equals(lastAssignedForegroundPath, foregroundPath, StringComparison.OrdinalIgnoreCase);
            if (changedApp)
            {
                lastAssignedForegroundPath = foregroundPath;
                manualOverridePath = null;
            }
            if (string.Equals(manualOverridePath, foregroundPath, StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(settings.activeMode, targetMode, StringComparison.OrdinalIgnoreCase)) return;
            if ((DateTime.UtcNow - lastAutomaticModeSwitch).TotalSeconds < 2) return;

            lastAutomaticModeSwitch = DateTime.UtcNow;
            RequestManagementMode(targetMode, true);
        }

        private void FlipManagementMode()
        {
            if (lastAssignedForegroundPath != null) manualOverridePath = lastAssignedForegroundPath;
            RequestManagementMode(workView ? "game" : "work", false);
        }

        private void RequestManagementMode(string mode, bool automatic)
        {
            if (workspaceFlipTimer.Enabled || modeSwitchInProgress || settings == null) return;
            if (!string.Equals(mode, "game", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mode, "work", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(settings.activeMode, mode, StringComparison.OrdinalIgnoreCase)) return;
            targetWorkView = string.Equals(mode, "work", StringComparison.OrdinalIgnoreCase);
            modeSwitchInProgress = true;
            System.Threading.Interlocked.Increment(ref statusRefreshGeneration);
            System.Threading.Interlocked.Exchange(ref statusRefreshPending, 1);
            SetFooter(automatic ? "Switching mode for the active application..." : "Switching mode...");
            SaveSettingsAsync(mode, delegate(bool saved)
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
            ShowManagementMode(targetWorkView);
            modeSwitchInProgress = false;
            RefreshStatus();
            RefreshModernView();
        }
        private void ShowManagementMode(bool showWork)
        {
            workView = showWork;
            gameSurface.Visible = !showWork;
            workSurface.Visible = showWork;
            if (showWork) workSurface.BringToFront(); else gameSurface.BringToFront();
            if (overlays != null) overlays.UpdateLayout();
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
            workSessionClock = SessionClock.Load(rootPath, "work", json);
            workModeValue.Text = workSessionClock.Paused ? "PAUSED" : workSessionStartedAt.HasValue ? "WORK ACTIVE" : "Ready / monitoring";
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
                SetLabelText(workCycleValue, GetCompletedCycles("work").ToString());
                return;
            }
            if (ApplyPausedTimerDisplay(workTimerStatePath, "work", workTimerPhaseValue, workMinutesLeftValue, workCycleValue)) return;
            if (!workSessionStartedAt.HasValue || !File.Exists(workTimerStatePath))
            {
                SetLabelText(workTimerPhaseValue, workSessionStartedAt.HasValue ? "Starting" : "Ready");
                SetLabelText(workMinutesLeftValue, TimerDuration.Format(TimerDuration.Resolve(settings.workTimerSeconds, settings.workTimerMinutes, 30)));
                SetLabelText(workCycleValue, GetCompletedCycles("work").ToString());
                return;
            }
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(workTimerStatePath));
                if (!string.Equals(Convert.ToString(state["Mode"]), "work", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
                DateTime deadline = DateTime.Parse(Convert.ToString(state["Deadline"]), null, System.Globalization.DateTimeStyles.RoundtripKind).ToLocalTime();
                int seconds = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.Now).TotalSeconds));
                SetLabelText(workTimerPhaseValue, Convert.ToString(state["Phase"]));
                SetLabelText(workMinutesLeftValue, TimerDuration.Format(seconds));
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
            TimeSpan elapsed = workSessionClock.ElapsedAt(DateTime.Now);
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
            using (Pen border = new Pen(ModernTheme.Border)) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
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
                if (!settingsMap.ContainsKey("timerPopupEnabled")) settings.timerPopupEnabled = true;
                if (!settingsMap.ContainsKey("timerSoundEnabled")) settings.timerSoundEnabled = true;
                if (!settingsMap.ContainsKey("sessionSummaryEnabled")) settings.sessionSummaryEnabled = true;
                if (!settingsMap.ContainsKey("automaticModeDetection")) settings.automaticModeDetection = false;
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
                    workApps = new string[0],
                    timerPopupEnabled = true,
                    timerSoundEnabled = true,
                    sessionSummaryEnabled = true,
                    automaticModeDetection = false
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
            timerMinutesInput.TotalSeconds = TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30);
            breakMinutesInput.TotalSeconds = TimerDuration.Resolve(settings.breakTimerSeconds, settings.breakTimerMinutes, 5);
            timerMinutesInput.Enabled = timerEnabledCheck.Checked;
            breakMinutesInput.Enabled = timerEnabledCheck.Checked;
            workTimerEnabledCheck.Checked = settings.workTimerEnabled;
            workTimerMinutesInput.TotalSeconds = TimerDuration.Resolve(settings.workTimerSeconds, settings.workTimerMinutes, 30);
            workBreakMinutesInput.TotalSeconds = TimerDuration.Resolve(settings.workBreakSeconds, settings.workBreakMinutes, 5);
            workTimerMinutesInput.Enabled = settings.workTimerEnabled;
            workBreakMinutesInput.Enabled = settings.workTimerEnabled;
            pauseWithEscapeCheck.Checked = settings.pauseGameWithEscape;
            timerPopupEnabled = settings.timerPopupEnabled;
            timerSoundEnabled = settings.timerSoundEnabled;
            customAlarmPath = settings.customAlarmPath ?? "";
            sessionSummaryEnabled = settings.sessionSummaryEnabled;
            automaticModeCheck.Checked = settings.automaticModeDetection;
            workAutomaticModeCheck.Checked = settings.automaticModeDetection;
            foldersList.Items.Clear();
            foreach (string app in settings.gameApps) foldersList.Items.Add(new GameDetectionTarget(app, true));
            foreach (string folder in settings.gameFolders) foldersList.Items.Add(new GameDetectionTarget(folder, false));
            workAppsList.Items.Clear();
            foreach (string app in settings.workApps) workAppsList.Items.Add(app);
            ShowManagementMode(string.Equals(settings.activeMode, "work", StringComparison.OrdinalIgnoreCase));
            UpdateTimerConfirmState();
            SyncOptions();
            selectionsDirty = false;
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
                if (new[] { timerMinutesInput, breakMinutesInput, workTimerMinutesInput, workBreakMinutesInput }.Any(input => input.TotalSeconds < 1))
                    throw new InvalidOperationException("Set each timer to at least one second.");
                next.gameTimerSeconds = timerMinutesInput.TotalSeconds;
                next.breakTimerSeconds = breakMinutesInput.TotalSeconds;
                next.gameTimerMinutes = Math.Max(1, (int)Math.Ceiling(next.gameTimerSeconds.Value / 60.0));
                next.breakTimerMinutes = Math.Max(1, (int)Math.Ceiling(next.breakTimerSeconds.Value / 60.0));
                next.workTimerEnabled = workTimerEnabledCheck.Checked;
                next.workTimerSeconds = workTimerMinutesInput.TotalSeconds;
                next.workBreakSeconds = workBreakMinutesInput.TotalSeconds;
                next.workTimerMinutes = Math.Max(1, (int)Math.Ceiling(next.workTimerSeconds.Value / 60.0));
                next.workBreakMinutes = Math.Max(1, (int)Math.Ceiling(next.workBreakSeconds.Value / 60.0));
                next.pauseGameWithEscape = pauseWithEscapeCheck.Checked;
                next.timerPopupEnabled = timerPopupEnabled;
                next.timerSoundEnabled = timerSoundEnabled;
                next.customAlarmPath = customAlarmPath;
                next.sessionSummaryEnabled = sessionSummaryEnabled;
                next.automaticModeDetection = automaticModeCheck.Checked;
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
            if (desktopBody != null) desktopBody.Enabled = enabled;
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
                SetFooter("Timer confirmed: " + TimerDuration.Format(timerMinutesInput.TotalSeconds) + " game / " + TimerDuration.Format(breakMinutesInput.TotalSeconds) + " break.");
                RefreshTimerDisplay(IsRuntimeMode("game"));
            });
        }

        private void OpenTimerReset(string mode)
        {
            using (TimerResetForm reset = new TimerResetForm(mode))
            {
                if (reset.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(reset.SelectedAction)) return;
                if (reset.SelectedAction == "reset-all" &&
                    ModernAlertForm.Show(this, "Reset timer and cycles", "Reset this mode's timer and completed cycles?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, "Reset", "Cancel") != DialogResult.Yes) return;
                ApplyTimerResetAsync(mode, reset.SelectedAction);
            }
        }

        private void ApplyTimerResetAsync(string mode, string action)
        {
            RunControlActionAsync("Resetting " + mode + " timer...", delegate
            {
                if (IsWatcherRunning()) SendTimerResetRequest(mode, action);
                else ApplyStoppedTimerReset(mode, action);
            }, delegate
            {
                SetFooter(action == "reset-timer" ? "Timer reset." :
                    action == "reset-cycles" ? "Completed cycles reset." : "Timer and completed cycles reset.");
            }, "The timer could not be reset.");
        }

        private void SendTimerResetRequest(string mode, string action)
        {
            string requestId = Guid.NewGuid().ToString("N");
            string temporaryPath = timerResetRequestPath + "." + requestId + ".tmp";
            int requesterPid;
            long requesterStartTicks;
            using (Process requester = Process.GetCurrentProcess())
            {
                requesterPid = requester.Id;
                requesterStartTicks = requester.StartTime.ToUniversalTime().Ticks;
            }
            if (File.Exists(timerResetAckPath)) File.Delete(timerResetAckPath);
            try
            {
                File.WriteAllText(temporaryPath, json.Serialize(new
                {
                    RequestId = requestId,
                    Mode = mode,
                    Action = action,
                    RequesterPid = requesterPid,
                    RequesterStartTicks = requesterStartTicks,
                    ExpiresUtc = DateTime.UtcNow.AddSeconds(15).ToString("o")
                }), new UTF8Encoding(false));
                if (File.Exists(timerResetRequestPath)) File.Delete(timerResetRequestPath);
                File.Move(temporaryPath, timerResetRequestPath);
                SignalWatcherWake();
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 10000)
                {
                    if (File.Exists(timerResetAckPath))
                    {
                        try
                        {
                            Dictionary<string, object> ack = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(timerResetAckPath));
                            object id, succeeded, message;
                            if (ack.TryGetValue("RequestId", out id) && string.Equals(Convert.ToString(id), requestId, StringComparison.OrdinalIgnoreCase))
                            {
                                if (ack.TryGetValue("Succeeded", out succeeded) && Convert.ToBoolean(succeeded)) return;
                                throw new InvalidOperationException(ack.TryGetValue("Message", out message) ? Convert.ToString(message) : "The watcher rejected the reset.");
                            }
                        }
                        catch (InvalidOperationException) { throw; }
                        catch { }
                    }
                    System.Threading.Thread.Sleep(100);
                }
                throw new TimeoutException("The watcher did not confirm the timer reset.");
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private void ApplyStoppedTimerReset(string mode, string action)
        {
            bool resetTimer = action == "reset-timer" || action == "reset-all";
            bool resetCycles = action == "reset-cycles" || action == "reset-all";
            if (!resetTimer && !resetCycles) throw new InvalidOperationException("Unsupported timer reset command.");

            Dictionary<string, object> counters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(timerCountersPath)) counters = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(timerCountersPath));
            }
            catch { counters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); }
            string counterKey = mode == "work" ? "Work" : "Game";
            if (resetCycles) counters[counterKey] = 0;
            if (!counters.ContainsKey("Game")) counters["Game"] = 0;
            if (!counters.ContainsKey("Work")) counters["Work"] = 0;
            counters["UpdatedAt"] = DateTime.Now.ToString("o");
            WriteJsonAtomically(timerCountersPath, counters);

            string path = mode == "work" ? workTimerStatePath : timerStatePath;
            Dictionary<string, object> state = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(path)) state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); }
            catch { state = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); }
            if (resetTimer || (resetCycles && state.Count > 0))
            {
                int cycles = resetCycles ? 0 : GetCompletedCycles(mode);
                state["Mode"] = mode;
                state["Cycle"] = cycles;
                if (resetTimer)
                {
                    int seconds = mode == "work" ? TimerDuration.Resolve(settings.workTimerSeconds, settings.workTimerMinutes, 30)
                        : TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30);
                    state["Phase"] = mode == "work" ? "Work" : "Game";
                    state["Minutes"] = seconds / 60.0;
                    state["Seconds"] = seconds;
                    state["Deadline"] = DateTime.Now.AddSeconds(seconds).ToString("o");
                    state["Paused"] = true;
                    state["RemainingSeconds"] = seconds;
                    state["Status"] = "Paused";
                }
                state["Revision"] = Guid.NewGuid().ToString("N");
                WriteJsonAtomically(path, state);
            }
        }

        private void WriteJsonAtomically(string path, object value)
        {
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, PrettyJson(json.Serialize(value)), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporaryPath, path, null, true);
                else File.Move(temporaryPath, path);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        private void UpdateTimerConfirmState()
        {
            if (confirmTimerButton == null || settings == null || timerEnabledCheck == null ||
                timerMinutesInput == null || breakMinutesInput == null) return;

            confirmTimerButton.Enabled =
                timerEnabledCheck.Checked != settings.gameTimerEnabled ||
                timerMinutesInput.TotalSeconds != TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30) ||
                breakMinutesInput.TotalSeconds != TimerDuration.Resolve(settings.breakTimerSeconds, settings.breakTimerMinutes, 5) ||
                workTimerEnabledCheck.Checked != settings.workTimerEnabled ||
                workTimerMinutesInput.TotalSeconds != TimerDuration.Resolve(settings.workTimerSeconds, settings.workTimerMinutes, 30) ||
                workBreakMinutesInput.TotalSeconds != TimerDuration.Resolve(settings.workBreakSeconds, settings.workBreakMinutes, 5) ||
                pauseWithEscapeCheck.Checked != settings.pauseGameWithEscape ||
                automaticModeCheck.Checked != settings.automaticModeDetection ||
                (popupOption != null && popupOption.Checked != settings.timerPopupEnabled) ||
                (soundOption != null && soundOption.Checked != settings.timerSoundEnabled);
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

        private void OpenDataOptions()
        {
            bool oldPopup = timerPopupEnabled;
            bool oldSound = timerSoundEnabled;
            bool oldSummary = sessionSummaryEnabled;
            using (DataOptionsForm options = new DataOptionsForm(oldPopup, oldSound, oldSummary, ExportPortableBackup, ImportPortableBackup))
            {
                if (options.ShowDialog(this) != DialogResult.OK) return;
                timerPopupEnabled = options.TimerPopupEnabled;
                timerSoundEnabled = options.TimerSoundEnabled;
                sessionSummaryEnabled = options.SessionSummaryEnabled;
                SaveSettingsAsync(null, delegate(bool saved)
                {
                    if (!saved)
                    {
                        timerPopupEnabled = oldPopup;
                        timerSoundEnabled = oldSound;
                        sessionSummaryEnabled = oldSummary;
                    }
                    else SetFooter("Notification options saved.");
                });
            }
        }

        private void ExportPortableBackup()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save Game Management backup";
                dialog.Filter = "Game Management backup (*.gmbak)|*.gmbak";
                dialog.DefaultExt = "gmbak";
                dialog.AddExtension = true;
                dialog.FileName = "GameManagement-Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".gmbak";
                if (dialog.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (FileStream stream = new FileStream(dialog.FileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create, false))
                    {
                        WriteBackupEntry(archive, "backup-manifest.json", json.Serialize(new
                        {
                            FormatVersion = 1,
                            AppVersion = "5.0.2",
                            CreatedAt = DateTime.Now.ToString("o")
                        }));
                        WriteBackupEntry(archive, "settings.json", File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : json.Serialize(settings));
                        if (File.Exists(historyPath)) WriteBackupEntry(archive, "GameSessionHistory.txt", File.ReadAllText(historyPath));
                    }
                    RetroMessage("Backup saved. It contains your settings and session history.", "Game Management");
                }
                catch (Exception ex) { ShowError("The backup could not be created.\r\n\r\n" + ex.Message); }
            }
        }

        private static void WriteBackupEntry(ZipArchive archive, string name, string contents)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false))) writer.Write(contents ?? "");
        }

        private void ImportPortableBackup()
        {
            if (File.Exists(engineEnabledPath) || IsWatcherRunning())
            {
                RetroMessage("Pause Game Management before restoring a backup. This protects any session that is currently being recorded.", "Game Management");
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Restore Game Management backup";
                dialog.Filter = "Game Management backup (*.gmbak)|*.gmbak|All files (*.*)|*.*";
                if (dialog.ShowDialog() != DialogResult.OK) return;
                if (ModernAlertForm.Show(this, "Restore backup", "Restore settings and session history from this backup?\r\n\r\nCurrent settings and history will be replaced.", MessageBoxButtons.YesNo, MessageBoxIcon.Question, "Restore", "Cancel") != DialogResult.Yes) return;
                try
                {
                    string restoredSettings;
                    string restoredHistory = "";
                    using (FileStream stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
                    {
                        ZipArchiveEntry manifest = archive.GetEntry("backup-manifest.json");
                        ZipArchiveEntry settingsEntry = archive.GetEntry("settings.json");
                        if (manifest == null || settingsEntry == null) throw new InvalidDataException("This is not a valid Game Management backup.");
                        if (settingsEntry.Length > 5 * 1024 * 1024) throw new InvalidDataException("The settings file in the backup is too large.");
                        restoredSettings = ReadBackupEntry(settingsEntry);
                        GameSettings validated = json.Deserialize<GameSettings>(restoredSettings);
                        if (validated == null) throw new InvalidDataException("The backup settings are invalid.");
                        ZipArchiveEntry historyEntry = archive.GetEntry("GameSessionHistory.txt");
                        if (historyEntry != null)
                        {
                            if (historyEntry.Length > 100 * 1024 * 1024) throw new InvalidDataException("The history file in the backup is too large.");
                            restoredHistory = ReadBackupEntry(historyEntry);
                        }
                    }
                    bool hadSettings = File.Exists(settingsPath);
                    bool hadHistory = File.Exists(historyPath);
                    string previousSettings = hadSettings ? File.ReadAllText(settingsPath) : null;
                    string previousHistory = hadHistory ? File.ReadAllText(historyPath) : null;
                    try
                    {
                        WriteFileAtomically(settingsPath, restoredSettings);
                        WriteFileAtomically(historyPath, restoredHistory);
                    }
                    catch
                    {
                        try
                        {
                            if (hadSettings) WriteFileAtomically(settingsPath, previousSettings); else if (File.Exists(settingsPath)) File.Delete(settingsPath);
                            if (hadHistory) WriteFileAtomically(historyPath, previousHistory); else if (File.Exists(historyPath)) File.Delete(historyPath);
                        }
                        catch { }
                        throw;
                    }
                    LoadSettings();
                    SetFooter("Backup restored. Use Enable when you are ready to monitor apps.");
                    RetroMessage("Backup restored successfully. Game Management remains paused until you choose Enable.", "Game Management");
                }
                catch (Exception ex) { ShowError("The backup could not be restored. No running session was changed.\r\n\r\n" + ex.Message); }
            }
        }

        private static string ReadBackupEntry(ZipArchiveEntry entry)
        {
            using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8, true)) return reader.ReadToEnd();
        }

        private static void WriteFileAtomically(string targetPath, string contents)
        {
            string temporaryPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, contents ?? "", new UTF8Encoding(false));
                if (File.Exists(targetPath)) File.Replace(temporaryPath, targetPath, null, true);
                else File.Move(temporaryPath, targetPath);
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
                if (!IsWatcherRunning()) RunPowerShell(watcherPath, false);
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
                RunPowerShell(pausePath, true);
            }, delegate
            {
                SetFooter("Game Management paused; timer frozen and system settings restored.");
                if (showMessage) RetroMessage("Game Management is paused. The timer is frozen and will continue from the same time when you enable it again.", "Game Management");
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
            if (PreviewMode || IsDisposed || !IsHandleCreated || modeSwitchInProgress) return;
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
                // Runtime state is authoritative. A matching power plan can remain selected after a
                // crash or manual change and must never make an idle watcher look GAME ACTIVE.
                snapshot.Managed = gameStateActive;
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
                UpdateDashboardActionState(enableButton.Enabled, pauseButton.Enabled, true);
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

        private int GetCompletedCycles(string mode)
        {
            try
            {
                if (!File.Exists(timerCountersPath)) return 0;
                Dictionary<string, object> counters = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(timerCountersPath));
                object value;
                string key = string.Equals(mode, "work", StringComparison.OrdinalIgnoreCase) ? "Work" : "Game";
                return counters.TryGetValue(key, out value) ? Math.Max(0, Convert.ToInt32(value)) : 0;
            }
            catch { return 0; }
        }

        private void RefreshTimerDisplay(bool managed)
        {
            if (timerValue == null || minutesLeftValue == null || cycleValue == null) return;
            if (settings == null || !settings.gameTimerEnabled)
            {
                SetLabelText(timerValue, "Off");
                SetLabelText(minutesLeftValue, "--:--");
                SetLabelText(cycleValue, GetCompletedCycles("game").ToString());
                return;
            }
            if (ApplyPausedTimerDisplay(timerStatePath, "game", timerValue, minutesLeftValue, cycleValue)) return;
            if (!managed)
            {
                int readySeconds = TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30);
                SetLabelText(timerValue, "Ready");
                SetLabelText(minutesLeftValue, TimerDuration.Format(readySeconds));
                SetLabelText(cycleValue, GetCompletedCycles("game").ToString());
                return;
            }
            if (!File.Exists(timerStatePath))
            {
                SetLabelText(timerValue, "Starting");
                SetLabelText(minutesLeftValue, TimerDuration.Format(TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30)));
                SetLabelText(cycleValue, GetCompletedCycles("game").ToString());
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
                int cycle = timerState.TryGetValue("Cycle", out cycleValueObject) ? Math.Max(0, Convert.ToInt32(cycleValueObject)) : GetCompletedCycles("game");
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

                SetLabelText(minutesLeftValue, TimerDuration.Format(totalSeconds));
            }
            catch
            {
                SetLabelText(timerValue, "Unavailable");
                SetLabelText(minutesLeftValue, "--:--");
                SetLabelText(cycleValue, "—");
            }
        }

        private bool ApplyPausedTimerDisplay(string path, string expectedMode, Label phaseLabel, Label remainingLabel, Label cycleLabel)
        {
            if (!File.Exists(path)) return false;
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                object pausedValue, modeValue, remainingValue, phaseValue, cycleValueObject;
                if (!state.TryGetValue("Paused", out pausedValue) || !Convert.ToBoolean(pausedValue) ||
                    !state.TryGetValue("Mode", out modeValue) ||
                    !string.Equals(Convert.ToString(modeValue), expectedMode, StringComparison.OrdinalIgnoreCase) ||
                    !state.TryGetValue("RemainingSeconds", out remainingValue)) return false;
                int seconds = Math.Max(0, (int)Math.Ceiling(Convert.ToDouble(remainingValue, System.Globalization.CultureInfo.InvariantCulture)));
                string phase = state.TryGetValue("Phase", out phaseValue) ? Convert.ToString(phaseValue) : (expectedMode == "work" ? "Work" : "Game");
                int cycle = state.TryGetValue("Cycle", out cycleValueObject) ? Math.Max(0, Convert.ToInt32(cycleValueObject)) : GetCompletedCycles(expectedMode);
                SetLabelText(phaseLabel, "Paused (" + phase + ")");
                SetLabelText(remainingLabel, TimerDuration.Format(seconds));
                SetLabelText(cycleLabel, cycle.ToString());
                return true;
            }
            catch { return false; }
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
            if (!ConfirmExitEdits()) return;
            exiting = true;
            refreshTimer.Stop();
            countdownTimer.Stop();
            performanceTimer.Stop();
            panelFlipTimer.Stop();
            foregroundModeTimer.Stop();
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
            if (!exiting && e.CloseReason == CloseReason.UserClosing && !ConfirmExitEdits())
            {
                e.Cancel = true;
                return;
            }
            StopModernViewRefresh();
            trayIcon.Dispose();
            refreshTimer.Dispose();
            countdownTimer.Dispose();
            performanceTimer.Dispose();
            panelFlipTimer.Dispose();
            workspaceFlipTimer.Dispose();
            foregroundModeTimer.Dispose();
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
            ModernAlertForm.Show(this, title, message, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowError(string message)
        {
            ModernAlertForm.Show(this, "Application error", message, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
