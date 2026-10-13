using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GameManagement.ReactHost
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try { Run(args); }
            catch (Exception ex)
            {
                MessageBox.Show("Game Management could not start.\r\n\r\n" + ex.Message, "Game Management", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void Run(string[] args)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            if (!File.Exists(Path.Combine(projectRoot, "GameManagement.ps1"))) projectRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            bool previewMode = args.Any(value => value.StartsWith("--preview-root=", StringComparison.OrdinalIgnoreCase)) ||
                AppDomain.CurrentDomain.FriendlyName.IndexOf(".Preview.", StringComparison.OrdinalIgnoreCase) >= 0;
            string dataRoot = ResolveDataRoot(projectRoot, args, previewMode);
            string webRoot = Path.Combine(projectRoot, "frontend", "dist");
            if (!Directory.Exists(webRoot)) { System.Windows.Forms.MessageBox.Show("Build frontend first with frontend\\pnpm build.", "Game Management React Host"); return; }
            int port = FindFreePort();
            using (ReactBridgeServer server = new ReactBridgeServer(projectRoot, dataRoot, webRoot, port, previewMode))
            {
                server.Start();
                string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameManagement", "DesktopWebView2");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ReactDesktopForm(server.Url, profile, projectRoot));
            }
        }

        private static string ResolveDataRoot(string projectRoot, string[] args, bool previewMode)
        {
            string preview = args.FirstOrDefault(value => value.StartsWith("--preview-root=", StringComparison.OrdinalIgnoreCase));
            if (preview != null || previewMode)
            {
                string relative = preview == null ? "preview-data" : preview.Substring(15);
                string candidate = Path.GetFullPath(Path.Combine(projectRoot, relative));
                string prefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Preview root must stay inside the project.");
                Directory.CreateDirectory(candidate); return candidate;
            }
            string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GameManagement");
            return File.Exists(Path.Combine(documents, "GameManagement.ps1")) ? documents : projectRoot;
        }
        private static int FindFreePort() { TcpListener listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
    }

    internal sealed class ReactDesktopForm : Form
    {
        private const int WmNcHitTest = 0x0084, WmNcLeftButtonDown = 0x00A1, HtCaption = 2, HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        private readonly WebView2 webView = new WebView2();
        private readonly Label loading = new Label();
        private readonly Uri startUri;
        private readonly string profilePath;

        public ReactDesktopForm(string url, string profilePath, string projectRoot)
        {
            startUri = new Uri(url);
            this.profilePath = profilePath;
            Text = "Game Management";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1027, 708);
            MinimumSize = new Size(900, 620);
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Color.FromArgb(248, 250, 252);
            AutoScaleMode = AutoScaleMode.Dpi;
            try
            {
                string iconPath = Path.Combine(projectRoot, "GameManagement.ico");
                if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            }
            catch { }

            webView.Dock = DockStyle.Fill;
            webView.DefaultBackgroundColor = BackColor;
            loading.Dock = DockStyle.Fill;
            loading.BackColor = BackColor;
            loading.ForeColor = Color.FromArgb(100, 116, 139);
            loading.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            loading.Text = "Loading Game Management…";
            loading.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(webView);
            Controls.Add(loading);
            Load += delegate { MaximizedBounds = Screen.FromControl(this).WorkingArea; };
            Shown += async delegate { await InitializeWebViewAsync(); };
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            try
            {
                Directory.CreateDirectory(profilePath);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, profilePath);
                await webView.EnsureCoreWebView2Async(environment);
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                webView.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs args) { args.Handled = true; };
                webView.CoreWebView2.NavigationCompleted += delegate { loading.Visible = false; };
                webView.Source = startUri;
            }
            catch (Exception ex)
            {
                loading.Text = "The desktop interface could not start." + Environment.NewLine + Environment.NewLine + ex.Message;
                MessageBox.Show(this, "The embedded desktop interface could not start.\r\n\r\n" + ex.Message, "Game Management", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string message; try { message = args.TryGetWebMessageAsString(); } catch { return; }
            if (message == "window:drag") { if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal; ReleaseCapture(); SendMessage(Handle, WmNcLeftButtonDown, new IntPtr(HtCaption), IntPtr.Zero); }
            else if (message == "window:minimize") WindowState = FormWindowState.Minimized;
            else if (message == "window:maximize") WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            else if (message == "window:close") Close();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref message); Point point = PointToClient(Cursor.Position); const int edge = 7;
                bool left = point.X <= edge, right = point.X >= ClientSize.Width - edge, top = point.Y <= edge, bottom = point.Y >= ClientSize.Height - edge;
                if (left && top) message.Result = new IntPtr(HtTopLeft); else if (right && top) message.Result = new IntPtr(HtTopRight); else if (left && bottom) message.Result = new IntPtr(HtBottomLeft); else if (right && bottom) message.Result = new IntPtr(HtBottomRight); else if (left) message.Result = new IntPtr(HtLeft); else if (right) message.Result = new IntPtr(HtRight); else if (top) message.Result = new IntPtr(HtTop); else if (bottom) message.Result = new IntPtr(HtBottom);
                return;
            }
            base.WndProc(ref message);
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs args)
        {
            Uri destination;
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out destination) ||
                !String.Equals(destination.Scheme, startUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(destination.Authority, startUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
        }
    }

    internal sealed class ReactBridgeServer : IDisposable
    {
        private readonly string projectRoot, dataRoot, webRoot, prefix;
        private readonly bool previewMode;
        private readonly TcpListener listener;
        private readonly object previewSoundSync = new object();
        private System.Media.SoundPlayer previewSoundPlayer;
        private Thread thread; private volatile bool stopping;
        private static readonly HashSet<string> AllowedSettingKeys = new HashSet<string>(new[] { "pollSeconds", "gameBrightnessPercent", "gameTimerEnabled", "gameTimerMinutes", "gameTimerSeconds", "breakTimerMinutes", "breakTimerSeconds", "workTimerEnabled", "workTimerMinutes", "workTimerSeconds", "workBreakMinutes", "workBreakSeconds", "pauseGameWithEscape", "timerPopupEnabled", "timerSoundEnabled", "customAlarmPath", "sessionSummaryEnabled", "automaticModeDetection", "activeMode", "workApps", "gameApps", "gameFolders", "excludedProcesses", "startWithWindows", "minimizeToTray" }, StringComparer.OrdinalIgnoreCase);
        private sealed class BridgeRequest
        {
            internal string Method, Path, Query, Body;
            internal readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        public ReactBridgeServer(string projectRoot, string dataRoot, string webRoot, int port, bool previewMode) { this.projectRoot = Path.GetFullPath(projectRoot); this.dataRoot = Path.GetFullPath(dataRoot); this.webRoot = Path.GetFullPath(webRoot); this.previewMode = previewMode; prefix = "http://127.0.0.1:" + port + "/"; listener = new TcpListener(IPAddress.Loopback, port); }
        public string Url { get { return prefix; } }
        public void Start() { listener.Start(); thread = new Thread(Loop) { IsBackground = true, Name = "GameManagementReactBridge" }; thread.Start(); }
        private void Loop()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(_ => Handle(client));
                }
                catch (SocketException) { if (!stopping) throw; }
                catch (ObjectDisposedException) { return; }
            }
        }
        private void Handle(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    client.ReceiveTimeout = 5000;
                    client.SendTimeout = 5000;
                    stream.ReadTimeout = 5000;
                    stream.WriteTimeout = 5000;
                    BridgeRequest request = ReadRequest(stream);
                    if (request.Method == "POST" && !IsSameOrigin(request)) { WriteResponse(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Cross-origin requests are not allowed."), true); return; }
                    if (previewMode && request.Method != "GET") { WriteResponse(stream, 403, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Preview mode is read-only."), true); return; }
                    if (request.Path == "/api/snapshot" && request.Method == "GET") WriteJson(stream, Snapshot());
                    else if (request.Path == "/api/app-icon" && request.Method == "GET") ServeApplicationIcon(stream, request);
                    else if (request.Path == "/api/command" && request.Method == "POST") WriteJson(stream, ExecuteCommand(ReadObject(request)));
                    else if (request.Path == "/api/settings" && request.Method == "POST") WriteJson(stream, SaveSettings(ReadObject(request)));
                    else if (request.Path == "/api/tasks" && request.Method == "POST") WriteJson(stream, SaveTasks(ReadArray(request)));
                    else if (request.Path == "/api/panels" && request.Method == "POST") WriteJson(stream, SavePanelPreferences(ReadObject(request)));
                    else if (request.Method == "GET") ServeStatic(stream, request.Path);
                    else WriteResponse(stream, 405, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Unsupported method."), true);
                }
                catch (Exception ex) { try { WriteResponse(stream, 500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ex.Message), true); } catch { } }
            }
        }
        private object Snapshot()
        {
            Dictionary<string, object> settings = ReadJson(Path.Combine(dataRoot, "settings.json")) ?? new Dictionary<string, object>();
            Dictionary<string, object> timers = new Dictionary<string, object> { { "work", ReadJson(Path.Combine(dataRoot, "work-timer-state.json")) }, { "game", ReadJson(Path.Combine(dataRoot, "timer-state.json")) } };
            Dictionary<string, object> sessions = new Dictionary<string, object> { { "work", ReadJson(Path.Combine(dataRoot, "work-session-state.json")) }, { "game", ReadJson(Path.Combine(dataRoot, "game-session-state.json")) } };
            Dictionary<string, object> taskDoc = ReadJson(Path.Combine(dataRoot, "tasks.json")); object tasks = new object[0]; if (taskDoc != null && taskDoc.ContainsKey("Items") && taskDoc["Items"] != null) tasks = taskDoc["Items"];
            Dictionary<string, object> panelPreferences = ReadJson(Path.Combine(dataRoot, "panel-preferences.json")) ?? new Dictionary<string, object>();
            if (!panelPreferences.ContainsKey("Version")) panelPreferences["Version"] = 1;
            if (!panelPreferences.ContainsKey("CalendarPinned")) panelPreferences["CalendarPinned"] = false;
            if (!panelPreferences.ContainsKey("TasksPinned")) panelPreferences["TasksPinned"] = false;
            return new { serverUtc = DateTime.UtcNow.ToString("o"), rootPath = dataRoot, watcherRunning = IsWatcherRunning(), engineEnabled = File.Exists(Path.Combine(dataRoot, "engine-enabled.flag")), settings, runtime = ReadJson(Path.Combine(dataRoot, "runtime-state.json")), timers, sessions, counters = ReadJson(Path.Combine(dataRoot, "timer-counters.json")) ?? new Dictionary<string, object>(), performance = ReadJson(Path.Combine(dataRoot, "performance-state.json")), tasks, history = ReadText(Path.Combine(dataRoot, "GameSessionHistory.txt"), 4 * 1024 * 1024), panelPreferences };
        }
        private object ExecuteCommand(Dictionary<string, object> command)
        {
            string name = Text(command, "name");
            if (previewMode) throw new InvalidOperationException("Live commands are disabled in isolated preview mode.");
            if (name == "openNative") { Process.Start(new ProcessStartInfo(Path.Combine(projectRoot, "GameManagement.exe")) { UseShellExecute = true, WorkingDirectory = projectRoot }); return Result(true, "Native management window opened."); }
            if (name == "chooseAlarmSound") return ChooseAlarmSound();
            if (name == "previewAlarmSound") return PreviewAlarmSound(Text(command, "path"));
            if (name == "pause") { RunPowerShell("Pause-GameManagement.ps1", true); return Result(true, "Game Management paused."); }
            if (name == "resume") { if (!File.Exists(Path.Combine(dataRoot, "GameManagement.ps1"))) throw new InvalidOperationException("The watcher script is unavailable in the active data root."); File.WriteAllText(Path.Combine(dataRoot, "engine-enabled.flag"), "enabled", new UTF8Encoding(false)); if (!IsWatcherRunning()) RunPowerShell("GameManagement.ps1", false); return Result(true, "Game Management resumed."); }
            if (name == "reset") { string mode = Text(command, "mode"), action = Text(command, "action"); if (mode != "work" && mode != "game") throw new InvalidOperationException("Invalid timer mode."); if (action != "reset-timer" && action != "reset-cycles" && action != "reset-all") throw new InvalidOperationException("Invalid reset action."); SendReset(mode, action); return Result(true, "Timer reset request accepted."); }
            throw new InvalidOperationException("Unsupported command.");
        }
        private object SaveSettings(Dictionary<string, object> submitted)
        {
            if (IsWatcherRunning()) throw new InvalidOperationException("Pause Game Management before saving settings so the timer checkpoint stays exact.");
            string path = Path.Combine(dataRoot, "settings.json"); Dictionary<string, object> current = ReadJson(path) ?? new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> item in submitted) if (AllowedSettingKeys.Contains(item.Key)) current[item.Key] = item.Value;
            if (submitted.ContainsKey("customAlarmPath")) current["customAlarmPath"] = ValidateAlarmPath(Convert.ToString(submitted["customAlarmPath"]), false);
            int poll = Convert.ToInt32(current.ContainsKey("pollSeconds") ? current["pollSeconds"] : 3); if (poll < 1 || poll > 3600) throw new InvalidOperationException("Scan interval is outside the supported range.");
            foreach (string key in new[] { "gameTimerSeconds", "breakTimerSeconds", "workTimerSeconds", "workBreakSeconds" }) if (current.ContainsKey(key)) { int seconds = Convert.ToInt32(current[key]); if (seconds < 1 || seconds > 86400) throw new InvalidOperationException("Timer duration is outside the supported range."); }
            WriteAtomic(path, CreateSerializer().Serialize(current)); return Result(true, "Settings saved. Resume when ready.");
        }
        private object ChooseAlarmSound()
        {
            string selected = null; Exception failure = null;
            Thread chooser = new Thread(new ThreadStart(delegate
            {
                try
                {
                    using (OpenFileDialog dialog = new OpenFileDialog())
                    {
                        dialog.Title = "Choose a Game Management alarm";
                        dialog.Filter = "Wave audio (*.wav)|*.wav";
                        dialog.CheckFileExists = true;
                        dialog.Multiselect = false;
                        dialog.RestoreDirectory = true;
                        if (dialog.ShowDialog() == DialogResult.OK) selected = ValidateAlarmPath(dialog.FileName, true);
                    }
                }
                catch (Exception ex) { failure = ex; }
            }));
            chooser.IsBackground = true; chooser.SetApartmentState(ApartmentState.STA); chooser.Start(); chooser.Join();
            if (failure != null) throw new InvalidOperationException("The alarm sound could not be selected.", failure);
            return new { ok = true, message = selected == null ? "No alarm sound was selected." : "Custom alarm selected.", path = selected ?? "" };
        }
        private object PreviewAlarmSound(string path)
        {
            string fullPath = ValidateAlarmPath(path, true);
            lock (previewSoundSync)
            {
                if (previewSoundPlayer != null) { try { previewSoundPlayer.Stop(); previewSoundPlayer.Dispose(); } catch { } }
                previewSoundPlayer = new System.Media.SoundPlayer(fullPath); previewSoundPlayer.Load(); previewSoundPlayer.Play();
            }
            return Result(true, "Playing " + Path.GetFileName(fullPath) + ".");
        }
        private static string ValidateAlarmPath(string path, bool requireExisting)
        {
            if (String.IsNullOrWhiteSpace(path)) return "";
            if (path.Length > 1024 || !Path.IsPathRooted(path) || !String.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Choose a local WAV audio file.");
            string fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)) throw new InvalidOperationException("Choose a WAV file stored on this computer.");
            if (requireExisting && !File.Exists(fullPath)) throw new FileNotFoundException("The selected alarm sound no longer exists.", fullPath);
            if (File.Exists(fullPath) && new FileInfo(fullPath).Length > 25L * 1024L * 1024L) throw new InvalidOperationException("The custom alarm must be 25 MB or smaller.");
            return fullPath;
        }
        private object SaveTasks(object[] submitted)
        {
            if (submitted.Length > 500) throw new InvalidOperationException("The task list is full."); HashSet<Guid> ids = new HashSet<Guid>(); List<object> clean = new List<object>();
            foreach (object value in submitted) { Dictionary<string, object> item = value as Dictionary<string, object>; Guid id; DateTime createdUtc; string idText = Text(item, "Id"), title = Text(item, "Title"), category = Text(item, "Category"); if (item == null || !Guid.TryParse(idText, out id) || !ids.Add(id) || String.IsNullOrWhiteSpace(title) || title.Length > 160 || (category != "Work" && category != "Game" && category != "Personal")) throw new InvalidOperationException("A task contains invalid data."); if (!DateTime.TryParse(Text(item, "CreatedUtc"), null, System.Globalization.DateTimeStyles.RoundtripKind, out createdUtc)) createdUtc = DateTime.UtcNow; clean.Add(new { Id = idText, Title = title.Trim(), Category = category, Completed = Bool(item, "Completed"), CreatedUtc = createdUtc.ToUniversalTime() }); }
            WriteAtomic(Path.Combine(dataRoot, "tasks.json"), CreateSerializer().Serialize(new { Version = 1, Items = clean })); return Result(true, "Tasks saved.");
        }
        private object SavePanelPreferences(Dictionary<string, object> submitted)
        {
            bool calendarPinned = Bool(submitted, "CalendarPinned"), tasksPinned = Bool(submitted, "TasksPinned");
            WriteAtomic(Path.Combine(dataRoot, "panel-preferences.json"), CreateSerializer().Serialize(new { Version = 1, CalendarPinned = calendarPinned, TasksPinned = tasksPinned }));
            return Result(true, "Panel preferences saved.");
        }
        private void SendReset(string mode, string action)
        {
            if (!IsWatcherRunning()) throw new InvalidOperationException("Resume Game Management before sending a timer reset request."); string requestId = Guid.NewGuid().ToString("N"), path = Path.Combine(dataRoot, "timer-reset-request.json"), temporary = path + "." + requestId + ".tmp"; long startTicks; using (Process process = Process.GetCurrentProcess()) startTicks = process.StartTime.ToUniversalTime().Ticks;
            WriteAtomic(temporary, CreateSerializer().Serialize(new { RequestId = requestId, Mode = mode, Action = action, RequesterPid = Process.GetCurrentProcess().Id, RequesterStartTicks = startTicks, ExpiresUtc = DateTime.UtcNow.AddSeconds(15).ToString("o") }), false); if (File.Exists(path)) File.Delete(path); File.Move(temporary, path);
            try { using (EventWaitHandle wake = EventWaitHandle.OpenExisting(@"Local\GameManagementWatcherWake_v1")) wake.Set(); } catch { }
        }
        private void RunPowerShell(string script, bool wait) { string path = Path.Combine(dataRoot, script); if (!File.Exists(path)) path = Path.Combine(projectRoot, script); if (!File.Exists(path)) throw new FileNotFoundException("Required backend script was not found.", script); ProcessStartInfo start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\"") { WorkingDirectory = dataRoot, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }; Process process = Process.Start(start); if (wait && process != null) { process.WaitForExit(); if (process.ExitCode != 0) throw new InvalidOperationException(script + " failed with exit code " + process.ExitCode + "."); process.Dispose(); } }
        private bool IsWatcherRunning() { if (previewMode) return false; try { using (Mutex watcher = Mutex.OpenExisting(@"Local\GameManagementWatcher_v1")) return true; } catch (WaitHandleCannotBeOpenedException) { return false; } catch (UnauthorizedAccessException) { return true; } }
        private bool IsSameOrigin(BridgeRequest request) { string expectedHost = new Uri(prefix).Authority, host, origin; if (!request.Headers.TryGetValue("Host", out host) || !String.Equals(host, expectedHost, StringComparison.OrdinalIgnoreCase)) return false; request.Headers.TryGetValue("Origin", out origin); return String.IsNullOrEmpty(origin) || String.Equals(origin.TrimEnd('/'), prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase); }
        private void ServeApplicationIcon(NetworkStream stream, BridgeRequest request)
        {
            string requested = QueryValue(request.Query, "path"), fullPath;
            try { fullPath = Path.GetFullPath(requested); }
            catch { WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Application icon not found."), true); return; }
            if (!IsConfiguredApplication(fullPath) || !File.Exists(fullPath)) { WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Application icon not found."), true); return; }
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(fullPath))
                {
                    if (icon == null) throw new InvalidOperationException("The application has no Windows icon.");
                    using (Bitmap bitmap = icon.ToBitmap())
                    using (MemoryStream png = new MemoryStream())
                    {
                        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        WriteResponse(stream, 200, "image/png", png.ToArray(), false);
                    }
                }
            }
            catch { WriteResponse(stream, 404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Application icon not found."), true); }
        }
        private bool IsConfiguredApplication(string fullPath)
        {
            Dictionary<string, object> settings = ReadJson(Path.Combine(dataRoot, "settings.json"));
            if (settings == null) return false;
            foreach (string key in new[] { "workApps", "gameApps" })
            {
                object value;
                if (!settings.TryGetValue(key, out value)) continue;
                IEnumerable values = value as IEnumerable;
                if (values == null || value is string) continue;
                foreach (object item in values)
                {
                    try { if (String.Equals(Path.GetFullPath(Convert.ToString(item)), fullPath, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { }
                }
            }
            return false;
        }
        private static string QueryValue(string query, string key)
        {
            foreach (string part in (query ?? "").Split('&'))
            {
                int equals = part.IndexOf('=');
                string name = equals < 0 ? part : part.Substring(0, equals);
                if (!String.Equals(Uri.UnescapeDataString(name.Replace("+", " ")), key, StringComparison.OrdinalIgnoreCase)) continue;
                string value = equals < 0 ? "" : part.Substring(equals + 1);
                return Uri.UnescapeDataString(value.Replace("+", " "));
            }
            return "";
        }
        private Dictionary<string, object> ReadObject(BridgeRequest request) { return CreateSerializer().Deserialize<Dictionary<string, object>>(request.Body ?? "") ?? new Dictionary<string, object>(); }
        private object[] ReadArray(BridgeRequest request) { object[] values = CreateSerializer().Deserialize<object[]>(request.Body ?? ""); return values ?? new object[0]; }
        private static BridgeRequest ReadRequest(NetworkStream stream)
        {
            const int maxHeaderBytes = 32768, maxBodyBytes = 1024 * 1024;
            List<byte> headerBytes = new List<byte>(); int matched = 0;
            while (headerBytes.Count < maxHeaderBytes)
            {
                int value = stream.ReadByte(); if (value < 0) throw new EndOfStreamException("The request ended before its headers were complete.");
                byte current = (byte)value; headerBytes.Add(current);
                byte expected = matched == 0 || matched == 2 ? (byte)'\r' : (byte)'\n';
                if (current == expected) { matched++; if (matched == 4) break; }
                else matched = current == (byte)'\r' ? 1 : 0;
            }
            if (matched != 4) throw new InvalidDataException("Request headers are too large.");
            string[] lines = Encoding.ASCII.GetString(headerBytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] first = lines[0].Split(' '); if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) throw new InvalidDataException("Invalid HTTP request line.");
            Uri requestUri; string rawPath = first[1]; if (Uri.TryCreate(rawPath, UriKind.Absolute, out requestUri)) rawPath = requestUri.PathAndQuery;
            int query = rawPath.IndexOf('?'); string rawQuery = query >= 0 ? rawPath.Substring(query + 1) : ""; if (query >= 0) rawPath = rawPath.Substring(0, query);
            BridgeRequest request = new BridgeRequest { Method = first[0].ToUpperInvariant(), Path = Uri.UnescapeDataString(rawPath), Query = rawQuery };
            for (int index = 1; index < lines.Length; index++) { int colon = lines[index].IndexOf(':'); if (colon > 0) request.Headers[lines[index].Substring(0, colon).Trim()] = lines[index].Substring(colon + 1).Trim(); }
            string contentLengthText, transferEncoding; int contentLength = 0;
            if (request.Headers.TryGetValue("Transfer-Encoding", out transferEncoding) && !String.IsNullOrWhiteSpace(transferEncoding)) throw new InvalidDataException("Chunked requests are not supported.");
            if (request.Headers.TryGetValue("Content-Length", out contentLengthText) && (!Int32.TryParse(contentLengthText, out contentLength) || contentLength < 0 || contentLength > maxBodyBytes)) throw new InvalidDataException("Request is too large.");
            byte[] body = new byte[contentLength]; int offset = 0; while (offset < body.Length) { int count = stream.Read(body, offset, body.Length - offset); if (count <= 0) throw new EndOfStreamException("The request body ended early."); offset += count; }
            request.Body = Encoding.UTF8.GetString(body); return request;
        }
        private static JavaScriptSerializer CreateSerializer() { return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }; }
        private Dictionary<string, object> ReadJson(string path) { if (!File.Exists(path)) return null; FileInfo info = new FileInfo(path); if (info.Length > 4 * 1024 * 1024) return null; try { return CreateSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)); } catch { return null; } }
        private static string ReadText(string path, int max) { if (!File.Exists(path) || new FileInfo(path).Length > max) return ""; try { return File.ReadAllText(path, Encoding.UTF8); } catch { return ""; } }
        private void ServeStatic(NetworkStream stream, string path) { string relative = path == "/" ? "index.html" : path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar); string file = Path.GetFullPath(Path.Combine(webRoot, relative)); string prefixPath = webRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; if (!file.StartsWith(prefixPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) file = Path.Combine(webRoot, "index.html"); WriteResponse(stream, 200, Mime(file), File.ReadAllBytes(file), false); }
        private void WriteJson(NetworkStream stream, object value) { WriteResponse(stream, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(CreateSerializer().Serialize(value)), true); }
        private static void WriteResponse(NetworkStream stream, int status, string contentType, byte[] bytes, bool noStore)
        {
            string reason = status == 200 ? "OK" : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : "Internal Server Error";
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + contentType + "\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n" + (noStore ? "Cache-Control: no-store\r\n" : "") + "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers); stream.Write(headerBytes, 0, headerBytes.Length); stream.Write(bytes, 0, bytes.Length); stream.Flush();
        }
        private void WriteAtomic(string path, string contents, bool replace = true) { Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = replace ? path + "." + Guid.NewGuid().ToString("N") + ".tmp" : path; File.WriteAllText(temp, contents, new UTF8Encoding(false)); if (!replace) return; if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path); }
        private static string Text(Dictionary<string, object> values, string key) { object value; return values != null && values.TryGetValue(key, out value) ? Convert.ToString(value) : ""; }
        private static bool Bool(Dictionary<string, object> values, string key) { object value; return values != null && values.TryGetValue(key, out value) && Convert.ToBoolean(value); }
        private static object Result(bool ok, string message) { return new { ok, message }; }
        private static string Mime(string path) { switch (Path.GetExtension(path).ToLowerInvariant()) { case ".html": return "text/html; charset=utf-8"; case ".js": return "text/javascript; charset=utf-8"; case ".css": return "text/css; charset=utf-8"; case ".svg": return "image/svg+xml"; case ".png": return "image/png"; default: return "application/octet-stream"; } }
        public void Dispose() { stopping = true; try { listener.Stop(); } catch { } lock (previewSoundSync) { if (previewSoundPlayer != null) { try { previewSoundPlayer.Stop(); previewSoundPlayer.Dispose(); } catch { } previewSoundPlayer = null; } } if (thread != null && thread.IsAlive) thread.Join(1000); }
    }
}
