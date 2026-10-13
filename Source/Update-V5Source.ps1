$ErrorActionPreference = 'Stop'
$sourcePath = Join-Path $PSScriptRoot 'GameManagement.cs'
$contents = [IO.File]::ReadAllText($sourcePath)
$contents = $contents.Replace('AssemblyVersion("3.3.0.0")','AssemblyVersion("5.0.0.0")').Replace('AssemblyFileVersion("3.3.0.0")','AssemblyFileVersion("5.0.0.0")')
$contents = $contents.Replace('internal sealed class MainForm : Form','internal sealed partial class MainForm : Form')
$start = $contents.IndexOf('        private void BuildWindow()')
$finish = $contents.IndexOf('        private void AddWorkTimerRow(', $start)
if ($start -ge 0 -and $finish -gt $start) { $contents = $contents.Substring(0,$start) + $contents.Substring($finish) }
$start = $contents.IndexOf('        private void BeginManagementModeFlip()')
$finish = $contents.IndexOf('        private void ShowManagementMode(', $start)
if ($start -ge 0 -and $finish -gt $start) {
    $replacement = @'
        private void BeginManagementModeFlip()
        {
            ShowManagementMode(targetWorkView);
            modeSwitchInProgress = false;
            RefreshStatus();
            RefreshModernView();
        }

'@
    $contents = $contents.Substring(0,$start) + $replacement + $contents.Substring($finish)
}
$contents = $contents.Replace('rootPath = File.Exists(Path.Combine(documentsRoot, "GameManagement.ps1"))', 'rootPath = PreviewMode ? (PreviewRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-data")) : File.Exists(Path.Combine(documentsRoot, "GameManagement.ps1"))')
$contents = $contents.Replace('            InitializeUiSignals();','            if (!PreviewMode) InitializeUiSignals();')
$contents = $contents.Replace('            LoadSettings();' + "`r`n" + '            RefreshStatus();', @'
            LoadSettings();
            SyncOptions();
            if (PreviewMode)
            {
                trayIcon.Visible = false;
                RefreshTimerDisplay(false);
                RefreshWorkTimerDisplay();
                RefreshModernView();
                return;
            }
            RefreshStatus();
'@)
$contents = $contents.Replace('            if (IsDisposed || !IsHandleCreated || modeSwitchInProgress) return;', '            if (PreviewMode || IsDisposed || !IsHandleCreated || modeSwitchInProgress) return;')
$contents = $contents.Replace('            if (settings == null || !settings.automaticModeDetection', '            if (PreviewMode || settings == null || !settings.automaticModeDetection')
$contents = $contents.Replace('            ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Raised);', '            using (Pen border = new Pen(ModernTheme.Border)) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);')
$contents = $contents.Replace('            if (workSurface != null) workSurface.Enabled = enabled;', "            if (workSurface != null) workSurface.Enabled = enabled;`r`n            if (desktopBody != null) desktopBody.Enabled = enabled;")
$contents = $contents.Replace('                            AppVersion = "3.1.1",','                            AppVersion = "5.0.0",')
$contents = $contents.Replace('            Application.SetCompatibleTextRenderingDefault(false);', @'
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
                Application.Run(new MainForm(false));
                return;
            }
'@)
$contents = $contents.Replace('                    form.Show();' + "`r`n" + '                    Application.DoEvents();' + "`r`n" + '                    if (args.Any', '                    form.Show();' + "`r`n" + '                    Application.DoEvents();' + "`r`n" + '                    foreach (string argument in args) { if (argument.StartsWith("--page=")) form.SetPageForPreview(argument.Substring(7)); if (argument.StartsWith("--panel=")) form.SetOverlayForPreview(argument.Substring(8)); }' + "`r`n" + '                    if (args.Any')
[IO.File]::WriteAllText($sourcePath, $contents, [Text.UTF8Encoding]::new($false))
$setupPath = Join-Path $PSScriptRoot 'GameManagementSetup.cs'
$setup = [IO.File]::ReadAllText($setupPath).Replace('3.3.0.0','5.0.0.0').Replace('"3.3.0"','"5.0.0"')
[IO.File]::WriteAllText($setupPath, $setup, [Text.UTF8Encoding]::new($false))
