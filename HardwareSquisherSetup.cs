using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HardwareSquisherInstaller
{
    internal static class Program
    {
        internal static readonly string[] PayloadNames =
        {
            "HardwareSquisher.exe",
            "HardwareSquisher.cs",
            "HardwareSquisher.ico",
            "HardwareSquisher.ps1",
            "Install-HardwareSquisher.ps1",
            "Pause-HardwareSquisher.ps1",
            "Undo-HardwareSquisher.ps1",
            "Uninstall-HardwareSquisher.ps1",
            "Test-HardwareSquisherSecurity.ps1",
            "README.txt",
            "TEMPERATURE-GUIDE.md",
            "settings.json"
        };

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--verify", StringComparison.OrdinalIgnoreCase))
                return VerifyPayloads() ? 0 : 1;
            if (args.Length > 0 && string.Equals(args[0], "--compatibility-test", StringComparison.OrdinalIgnoreCase))
                return HardwareCompatibility.RunSelfTest() ? 0 : 1;

            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length >= 2 && string.Equals(args[0], "--render", StringComparison.OrdinalIgnoreCase))
            {
                using (SetupForm form = new SetupForm())
                {
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap preview = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(preview, new Rectangle(0, 0, preview.Width, preview.Height));
                        preview.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }
            Application.Run(new SetupForm());
            return 0;
        }

        internal static Stream OpenPayload(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload." + name);
        }

        private static bool VerifyPayloads()
        {
            foreach (string name in PayloadNames)
            {
                using (Stream stream = OpenPayload(name))
                {
                    if (stream == null || stream.Length == 0) return false;
                }
            }
            return true;
        }
    }

    internal sealed class HardwareCompatibilityReport
    {
        internal readonly List<string> GpuNames = new List<string>();
        internal bool HasRtx20Or30Series;

        internal string DisplayText
        {
            get
            {
                if (HasRtx20Or30Series) return "NVIDIA RTX 20/30-series detected - compatible";
                if (GpuNames.Count > 0) return "GPU-independent Windows mode - compatible";
                return "GPU not reported - compatible generic mode";
            }
        }
    }

    internal static class HardwareCompatibility
    {
        private static readonly Regex Rtx20Or30Pattern = new Regex(
            @"\bRTX\s*(?:20|30)\d{2}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool IsRtx20Or30Series(string gpuName)
        {
            return !string.IsNullOrWhiteSpace(gpuName) && Rtx20Or30Pattern.IsMatch(gpuName);
        }

        internal static HardwareCompatibilityReport Inspect()
        {
            HardwareCompatibilityReport report = new HardwareCompatibilityReport();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                    {
                        string name = Convert.ToString(item["Name"]).Trim();
                        if (name.Length == 0) continue;
                        report.GpuNames.Add(name);
                        if (IsRtx20Or30Series(name)) report.HasRtx20Or30Series = true;
                    }
                }
            }
            catch
            {
                // Hardware reporting is informational and must never block setup.
            }
            return report;
        }

        internal static void WriteReport(string installRoot, HardwareCompatibilityReport report)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Hardware Squisher compatibility report");
            text.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
            text.AppendLine("Windows: " + Environment.OSVersion.VersionString);
            text.AppendLine("OS architecture: " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"));
            text.AppendLine("Compatibility mode: " + report.DisplayText);
            text.AppendLine("Detected display adapters:");
            if (report.GpuNames.Count == 0) text.AppendLine("- Not reported by Windows");
            foreach (string name in report.GpuNames) text.AppendLine("- " + name);
            text.AppendLine();
            text.AppendLine("The application does not change GPU clocks, voltages, drivers, firmware, or NVIDIA settings.");
            File.WriteAllText(Path.Combine(installRoot, "HARDWARE-COMPATIBILITY.txt"), text.ToString(), Encoding.UTF8);
        }

        internal static bool RunSelfTest()
        {
            string[] supported =
            {
                "NVIDIA GeForce RTX 2060",
                "NVIDIA GeForce RTX 2070 SUPER",
                "NVIDIA GeForce RTX 2080 Ti",
                "NVIDIA GeForce RTX 3050 Laptop GPU",
                "NVIDIA GeForce RTX 3060 Ti",
                "NVIDIA GeForce RTX 3070",
                "NVIDIA GeForce RTX 3080 Laptop GPU",
                "NVIDIA GeForce RTX 3090"
            };
            foreach (string name in supported)
                if (!IsRtx20Or30Series(name)) return false;

            string[] generic = { "NVIDIA GeForce RTX 4090", "AMD Radeon RX 6800 XT", "Intel Arc A770", "" };
            foreach (string name in generic)
                if (IsRtx20Or30Series(name)) return false;
            return true;
        }
    }

    internal sealed class SetupForm : Form
    {
        private const int WmNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;
        private readonly Button installButton;
        private readonly Button cancelButton;
        private readonly CheckBox launchCheck;
        private readonly ProgressBar progress;
        private readonly Label status;
        private bool installing;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        public SetupForm()
        {
            HardwareCompatibilityReport hardware = HardwareCompatibility.Inspect();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Text = "Hardware Squisher Setup";
            ClientSize = new Size(540, 340);
            MinimumSize = MaximumSize = Size;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(192, 192, 192);
            Font = new Font("Microsoft Sans Serif", 8.25F);
            Padding = new Padding(3);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = 28;
            titleBar.BackColor = Color.FromArgb(0, 0, 128);
            titleBar.MouseDown += DragWindow;
            Controls.Add(titleBar);

            Label title = new Label();
            title.Text = "▣  Hardware Squisher Setup";
            title.ForeColor = Color.White;
            title.Font = new Font(Font, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(5, 6);
            title.MouseDown += DragWindow;
            titleBar.Controls.Add(title);

            Button close = RetroButton("×", 24, 21);
            close.Dock = DockStyle.Right;
            close.Click += delegate { if (!installing) Close(); };
            titleBar.Controls.Add(close);

            Label heading = new Label();
            heading.Text = "HARDWARE SQUISHER";
            heading.Font = new Font("Arial", 23F, FontStyle.Bold);
            heading.AutoSize = true;
            heading.Location = new Point(28, 49);
            Controls.Add(heading);

            Label subheading = new Label();
            subheading.Text = "Windows 98-style Game Management and break controller";
            subheading.AutoSize = true;
            subheading.Location = new Point(31, 88);
            Controls.Add(subheading);

            GroupBox details = new GroupBox();
            details.Text = "Setup details";
            details.Location = new Point(28, 116);
            details.Size = new Size(484, 116);
            Controls.Add(details);

            PictureBox iconBox = new PictureBox();
            iconBox.Location = new Point(18, 27);
            iconBox.Size = new Size(64, 64);
            iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            iconBox.Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap();
            details.Controls.Add(iconBox);

            Label description = new Label();
            description.Text = "Install the application, background watcher, shortcuts,\r\nand Windows uninstall entry for your account.";
            description.AutoSize = true;
            description.Location = new Point(98, 29);
            details.Controls.Add(description);

            Label compatibility = new Label();
            compatibility.Text = "Hardware: " + hardware.DisplayText;
            compatibility.AutoEllipsis = true;
            compatibility.Location = new Point(98, 56);
            compatibility.Size = new Size(365, 18);
            details.Controls.Add(compatibility);

            Label destination = new Label();
            destination.Text = "Location: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GameBoost");
            destination.AutoEllipsis = true;
            destination.Location = new Point(98, 77);
            destination.Size = new Size(365, 24);
            details.Controls.Add(destination);

            launchCheck = new CheckBox();
            launchCheck.Text = "Launch Hardware Squisher after installation";
            launchCheck.Checked = true;
            launchCheck.AutoSize = true;
            launchCheck.Location = new Point(31, 244);
            Controls.Add(launchCheck);

            progress = new ProgressBar();
            progress.Location = new Point(28, 270);
            progress.Size = new Size(484, 18);
            progress.Minimum = 0;
            progress.Maximum = 100;
            Controls.Add(progress);

            status = new Label();
            status.Text = "Ready to install.";
            status.BorderStyle = BorderStyle.Fixed3D;
            status.Location = new Point(28, 298);
            status.Size = new Size(286, 24);
            status.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(status);

            installButton = RetroButton("Install", 92, 25);
            installButton.Location = new Point(320, 297);
            installButton.Click += InstallClicked;
            Controls.Add(installButton);

            cancelButton = RetroButton("Cancel", 92, 25);
            cancelButton.Location = new Point(420, 297);
            cancelButton.Click += delegate { Close(); };
            Controls.Add(cancelButton);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Raised);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (installing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        private static Button RetroButton(string text, int width, int height)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(width, height);
            button.FlatStyle = FlatStyle.Standard;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.FromArgb(192, 192, 192);
            return button;
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || installing) return;
            ReleaseCapture();
            SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            installing = true;
            installButton.Enabled = false;
            cancelButton.Enabled = false;
            launchCheck.Enabled = false;
            try
            {
                InstallApplication();
                progress.Value = 100;
                status.Text = "Installation complete.";
                installing = false;
                MessageBox.Show(this, "Hardware Squisher was installed successfully.", "Hardware Squisher Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                installing = false;
                installButton.Enabled = true;
                cancelButton.Enabled = true;
                launchCheck.Enabled = true;
                status.Text = "Installation did not complete.";
                MessageBox.Show(this, ex.Message, "Hardware Squisher Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetProgress(int value, string text)
        {
            progress.Value = value;
            status.Text = text;
            status.Refresh();
            progress.Refresh();
            Application.DoEvents();
        }

        private void InstallApplication()
        {
            string installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GameBoost");
            Directory.CreateDirectory(installRoot);
            HardwareCompatibilityReport hardware = HardwareCompatibility.Inspect();

            SetProgress(8, "Closing the previous application...");
            foreach (Process process in Process.GetProcessesByName("HardwareSquisher"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch { }
                finally { process.Dispose(); }
            }

            SetProgress(20, "Copying application files...");
            foreach (string name in Program.PayloadNames)
            {
                string destination = Path.Combine(installRoot, name);
                if (string.Equals(name, "settings.json", StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
                    continue;
                ExtractPayload(name, destination);
            }
            HardwareCompatibility.WriteReport(installRoot, hardware);

            SetProgress(55, "Configuring Game Management power mode...");
            RunInstallerScript(Path.Combine(installRoot, "Install-HardwareSquisher.ps1"));

            SetProgress(78, "Creating shortcuts...");
            string appPath = Path.Combine(installRoot, "HardwareSquisher.exe");
            string iconPath = Path.Combine(installRoot, "HardwareSquisher.ico");
            string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Hardware Squisher");
            Directory.CreateDirectory(startMenuFolder);
            CreateShortcut(Path.Combine(startMenuFolder, "Hardware Squisher.lnk"), appPath, installRoot, iconPath, "Hardware Squisher");
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Hardware Squisher.lnk"), appPath, installRoot, iconPath, "Hardware Squisher");

            SetProgress(90, "Registering uninstall support...");
            RegisterUninstaller(installRoot, iconPath);

            if (launchCheck.Checked)
                Process.Start(new ProcessStartInfo(appPath) { WorkingDirectory = installRoot, UseShellExecute = true });
        }

        private static void ExtractPayload(string name, string destination)
        {
            using (Stream input = Program.OpenPayload(name))
            {
                if (input == null) throw new InvalidDataException("Installer payload is missing: " + name);
                string temporary = destination + ".setup-new";
                using (FileStream output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    input.CopyTo(output);
                File.Copy(temporary, destination, true);
                File.Delete(temporary);
            }
        }

        private static void RunInstallerScript(string scriptPath)
        {
            ProcessStartInfo info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(90000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Setup timed out while configuring Windows.");
                }
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Windows configuration failed.\r\n\r\n" + (string.IsNullOrWhiteSpace(error) ? output : error));
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory, string iconPath, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows shortcut support is unavailable.");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void RegisterUninstaller(string installRoot, string iconPath)
        {
            string uninstallScript = Path.Combine(installRoot, "Uninstall-HardwareSquisher.ps1");
            string command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + uninstallScript + "\"";
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HardwareSquisher"))
            {
                if (key == null) throw new InvalidOperationException("Could not register uninstall support.");
                key.SetValue("DisplayName", "Hardware Squisher");
                key.SetValue("DisplayVersion", "1.13.0");
                key.SetValue("Publisher", "Hardware Squisher");
                key.SetValue("InstallLocation", installRoot);
                key.SetValue("DisplayIcon", iconPath);
                key.SetValue("UninstallString", command);
                key.SetValue("QuietUninstallString", command + " -Quiet");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }
    }
}
