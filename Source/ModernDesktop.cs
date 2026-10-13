using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GameManagement
{
    internal sealed partial class MainForm
    {
        internal static bool PreviewMode;
        internal static string PreviewRoot;
        private Panel desktopBody, homePage, applicationsPage, timersPage, optionsPage;
        private ModernReportsView reportsPage;
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private string currentPage = "Home";
        private OverlayPanels overlays;
        private DataGridView gameDashboardGrid, workDashboardGrid, managementGrid;
        private bool applicationWorkTab;
        private TextBox applicationSearch;
        private Label gameElapsedValue, applicationCount;
        private ModernProgress gameProgress, workProgress;
        private readonly Timer modernRefresh = new Timer();
        private bool modernViewReady, modernViewClosing, modernViewResourcesDisposed;
        private string modernViewBindingWarning;
        private readonly Dictionary<string, Image> applicationIcons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private CheckBox popupOption, soundOption, summaryOption;
        private CheckBox timerPopupOption, timerSoundOption;
        private Button workTab, gameTab;
        private bool selectionsDirty;
        private readonly Dictionary<bool, ModernSessionOverview> sessionOverviews = new Dictionary<bool, ModernSessionOverview>();
        private readonly Dictionary<bool, ModernTimerSettingsSummary> dashboardSettings = new Dictionary<bool, ModernTimerSettingsSummary>();
        private readonly Dictionary<bool, Label> dashboardApplications = new Dictionary<bool, Label>();
        private readonly Dictionary<bool, StatusBadge> modeBadges = new Dictionary<bool, StatusBadge>();
        private readonly Dictionary<bool, StatusBadge> thermalBadges = new Dictionary<bool, StatusBadge>();
        private readonly Dictionary<bool, Label> displayPhases = new Dictionary<bool, Label>();
        private readonly Dictionary<bool, Label> gpuDisplays = new Dictionary<bool, Label>();
        private readonly Dictionary<bool, Button> dashboardResumeButtons = new Dictionary<bool, Button>();
        private readonly Dictionary<bool, Button> dashboardPauseButtons = new Dictionary<bool, Button>();
        private readonly Dictionary<bool, Button> dashboardResetButtons = new Dictionary<bool, Button>();
        private int Ui(int value) { return ModernTheme.Px(this, value); }

        private void BuildWindow()
        {
            SuspendLayout(); Text = "Game Management"; Font = ModernTheme.Font(9);
            BackColor = ModernTheme.Background; ForeColor = ModernTheme.Text;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); ClientSize = new Size(1280, 880);
            MinimumSize = new Size(800, 600); StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None; Padding = new Padding(1); ShowInTaskbar = true;
            Icon = LoadApplicationIcon(); KeyPreview = true;
            MaximizedBounds = Screen.FromControl(this).WorkingArea;

            Panel titleBar = new Panel { Dock = DockStyle.Top, Height = ModernTheme.TitleBarHeight, BackColor = ModernTheme.Background, AccessibleName = "Application title and navigation" };
            Control appIcon = ModernTheme.Icon("\uE7FC", ModernTheme.Work, 18); appIcon.Bounds = new Rectangle(16, 15, 18, 18);
            Label title = ModernTheme.Label("Game Management", 10, true); title.Location = new Point(44, 15);
            titleBar.Controls.Add(appIcon); titleBar.Controls.Add(title);
            titleBar.MouseDown += DragWindow; title.MouseDown += DragWindow;
            titleBar.DoubleClick += delegate { ToggleMaximize(); };
            Button close = ModernTheme.Button("×", delegate { HideToTray(); }); close.AccessibleName = "Close window";
            Button maximize = ModernTheme.Button("□", ToggleMaximize); maximize.AccessibleName = "Maximize or restore window";
            Button minimize = ModernTheme.Button("−", delegate { WindowState = FormWindowState.Minimized; }); minimize.AccessibleName = "Minimize window";
            Button[] captionButtons = { minimize, maximize, close };
            foreach (Button button in captionButtons) { button.Size = new Size(46, 32); button.Font = ModernTheme.Font(12); button.BackColor = ModernTheme.Background; button.Anchor = AnchorStyles.Top | AnchorStyles.Right; titleBar.Controls.Add(button); }
            Controls.Add(titleBar);

            Panel dock = new Panel { Size = new Size(480, ModernTheme.DockHeight), BackColor = ModernTheme.Background };
            string[] names = { "Home", "Applications", "Timer Settings", "Reports", "Options" };
            string[] glyphs = { "\uE80F", "\uE8A7", "\uE916", "\uE9D9", "\uE713" };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i]; DockButton button = new DockButton { Text = name, Glyph = glyphs[i], Bounds = new Rectangle(i * 96, 0, 96, ModernTheme.DockHeight), AccessibleName = name };
                button.Click += delegate { Navigate(name); }; navigation.Add(name, button); dock.Controls.Add(button);
            }
            titleBar.Controls.Add(dock);
            titleBar.Resize += delegate
            {
                int captionWidth = Ui(46), captionTop = Ui(8), captionHeight = Ui(32);
                close.SetBounds(titleBar.ClientSize.Width - captionWidth, captionTop, captionWidth, captionHeight);
                maximize.SetBounds(close.Left - captionWidth, captionTop, captionWidth, captionHeight);
                minimize.SetBounds(maximize.Left - captionWidth, captionTop, captionWidth, captionHeight);
                int freeLeft = title.Right + Ui(12), freeRight = minimize.Left - Ui(8);
                int dockWidth = Math.Max(Ui(1), Math.Min(Ui(480), freeRight - freeLeft));
                int dockLeft = Math.Max(freeLeft, Math.Min((titleBar.Width - dockWidth) / 2, freeRight - dockWidth));
                dock.SetBounds(dockLeft, Ui(2), dockWidth, Ui(44));
                int slot = dockWidth / 5; int i = 0;
                foreach (Button button in navigation.Values) button.SetBounds(i++ * slot, 0, slot, Ui(44));
            };
            footerLabel = ModernTheme.Label("Ready.", 8); footerLabel.AutoSize = false; footerLabel.Dock = DockStyle.Bottom; footerLabel.Height = 24; footerLabel.Padding = new Padding(16, 0, 8, 0); footerLabel.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(footerLabel);
            desktopBody = new Panel { Dock = DockStyle.Fill, BackColor = ModernTheme.Background }; Controls.Add(desktopBody); desktopBody.BringToFront();
            homePage = new Panel { Dock = DockStyle.Fill, BackColor = ModernTheme.Background }; desktopBody.Controls.Add(homePage);
            gameSurface = Dashboard(false); workSurface = Dashboard(true);
            homePage.Controls.Add(gameSurface); homePage.Controls.Add(workSurface);
            BuildHiddenState();
            applicationsPage = BuildApplicationsPage(); desktopBody.Controls.Add(applicationsPage);
            timersPage = BuildTimersPage(); desktopBody.Controls.Add(timersPage);
            optionsPage = BuildOptionsPage(); desktopBody.Controls.Add(optionsPage);
            reportsPage = new ModernReportsView(historyPath) { Dock = DockStyle.Fill }; desktopBody.Controls.Add(reportsPage);
            overlays = new OverlayPanels(homePage, rootPath, !PreviewMode);
            overlays.RangeChanged += delegate(DateTime from, DateTime to) { reportsPage.SetDateRange(from, to); foreach (ModernSessionOverview overview in sessionOverviews.Values) overview.SetRange(from, to); };
            overlays.ViewReportsRequested += delegate { reportsPage.SetDateRange(overlays.RangeStart, overlays.RangeEnd); Navigate("Reports"); };
            homePage.Resize += delegate { overlays.UpdateLayout(); };
            Navigate("Home");
            modernRefresh.Interval = 1000;
            modernRefresh.Tick += delegate { if (Visible) RefreshModernView(); };
            FormClosing += OnFormClosing;
            FormClosed += delegate { DisposeModernViewResources(); };
            Shown += delegate
            {
                if (PreviewMode) { RefreshModernView(); return; }
                StartUiSignalListener(); RefreshStatus();
                if (enableOnStart) EnableGameManagement(false);
                if (showForBreakOnStart) ShowForBreak(); else if (hideOnStart) BeginInvoke(new MethodInvoker(HideAfterBreak));
                RefreshTemperatureMetrics();
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5) { Navigate(names[(int)e.KeyCode - (int)Keys.D1]); e.Handled = true; }
            };
            ResumeLayout(true);
        }

        private void BuildHiddenState()
        {
            // Existing backend binds these controls; the redesigned pages own presentation.
            foldersList = new ListBox(); workAppsList = new ListBox();
            planValue = ModernTheme.Label("Unavailable"); brightnessValue = ModernTheme.Label("Unavailable"); watcherValue = ModernTheme.Label("Stopped");
            workFooterLabel = ModernTheme.Label("Ready."); statusLamp = new Panel();
            statusGroup = new GroupBox(); statusSurface = new Panel { Size = new Size(600, 220) }; statusFrontPanel = new Panel { Dock = DockStyle.Fill }; flowerPanel = new PixelGerberaPanel { Dock = DockStyle.Fill, Visible = false };
            statusGroup.Controls.Add(statusSurface); statusSurface.Controls.Add(statusFrontPanel); statusSurface.Controls.Add(flowerPanel);
            flipStatusButton = ModernTheme.Button("Flowers", delegate { ShowFlowerPanel(true); }); flowerFlipButton = ModernTheme.Button("Status", delegate { ShowFlowerPanel(false); });
            statusFrontPanel.Controls.Add(flipStatusButton); flowerPanel.Controls.Add(flowerFlipButton);
            workAutomaticModeCheck = new CheckBox();
        }

        private Panel ScrollPage(string heading, string description, out Panel content)
        {
            Panel page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ModernTheme.Background };
            content = new Panel { Location = new Point(32, 24), Size = new Size(1170, 740) }; page.Controls.Add(content);
            Label title = ModernTheme.Label(heading, 22, true); title.Location = Point.Empty; content.Controls.Add(title);
            Label subtitle = ModernTheme.Label(description, 9); subtitle.Location = new Point(0, 44); content.Controls.Add(subtitle);
            Panel owned = content; page.Resize += delegate { owned.Width = Math.Max(Ui(700), page.ClientSize.Width - Ui(80)); page.AutoScrollMinSize = new Size(owned.Right + Ui(24), owned.Bottom + Ui(24)); };
            return page;
        }

        private Panel BuildApplicationsPage()
        {
            Panel content; Panel page = ScrollPage("Applications", "Choose the applications that start your work and game sessions.", out content);
            workTab = ModernTheme.Button("Work Applications", delegate { applicationWorkTab = true; RefreshApplicationPage(); });
            gameTab = ModernTheme.Button("Game Applications", delegate { applicationWorkTab = false; RefreshApplicationPage(); });
            workTab.SetBounds(0, 88, 184, 40); gameTab.SetBounds(192, 88, 184, 40); content.Controls.Add(workTab); content.Controls.Add(gameTab);
            ModernCard card = new ModernCard { Location = new Point(0, 144), Height = 470 }; content.Controls.Add(card);
            applicationSearch = new TextBox { Font = ModernTheme.Font(10), BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Search applications", Location = new Point(16, 22), Width = 260 };
            applicationSearch.TextChanged += delegate { RefreshApplicationPage(); }; card.Controls.Add(applicationSearch);
            applicationCount = ModernTheme.Label("0 applications"); applicationCount.Location = new Point(292, 24); card.Controls.Add(applicationCount);
            managementGrid = AppGrid(); managementGrid.Location = new Point(16, 72); card.Controls.Add(managementGrid);
            managementGrid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e) { ApplicationAction(managementGrid, e, applicationWorkTab); };
            FlowLayoutPanel actions = new FlowLayoutPanel { Location = new Point(16, 402), Height = 52, WrapContents = false };
            actions.Controls.Add(ModernTheme.Button("Browse executable", delegate { if (applicationWorkTab) AddWorkApplication(); else BrowseGameApplication(); MarkApplicationsChanged(); }));
            actions.Controls.Add(ModernTheme.Button("Discover installed", delegate { if (applicationWorkTab) ChooseInstalledWorkApplication(); else ChooseInstalledGameApplication(); MarkApplicationsChanged(); }));
            actions.Controls.Add(ModernTheme.Button("Add library folder", delegate { applicationWorkTab = false; AddFolder(); MarkApplicationsChanged(); }));
            actions.Controls.Add(ModernTheme.Button("Save selections", delegate { SaveAllSettings(); }, true)); card.Controls.Add(actions);
            Label note = ModernTheme.Label("Selections use the full executable path. Game library folders remain supported.", 9); note.Location = new Point(0, 638); content.Controls.Add(note);
            page.Resize += delegate { card.Width = content.Width; managementGrid.Size = new Size(card.Width - Ui(32), Ui(314)); actions.Width = card.Width - Ui(32); };
            return page;
        }

        private DataGridView AppGrid()
        {
            DataGridView grid = new DataGridView(); ModernTheme.Grid(grid);
            grid.Columns.Add(new DataGridViewImageColumn { Name = "Icon", HeaderText = "", Width = 40, ImageLayout = DataGridViewImageCellLayout.Zoom, MinimumWidth = 40 });
            grid.Columns.Add("Application", "Application"); grid.Columns[1].Width = 160; grid.Columns[1].MinimumWidth = 120;
            grid.Columns.Add("Path", "Executable path / folder"); grid.Columns[2].MinimumWidth = 220; grid.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add("Status", "Status"); grid.Columns[3].Width = 100; grid.Columns[3].MinimumWidth = 88;
            grid.Columns.Add(new ModernActionColumn("Launch", "\uE768", "Launch application"));
            grid.Columns.Add(new ModernActionColumn("Configure", "\uE8B7", "Open executable location"));
            grid.Columns.Add(new ModernActionColumn("Remove", "\uE74D", "Remove application"));
            grid.Columns[4].HeaderText = "";
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex != -1 || e.ColumnIndex < 4) return;
                e.PaintBackground(e.ClipBounds, false); e.Handled = true;
            };
            grid.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (grid.Columns.Count < 7) return;
                Rectangle first = grid.GetCellDisplayRectangle(4, -1, false), last = grid.GetCellDisplayRectangle(6, -1, false);
                Rectangle heading = Rectangle.Union(first, last);
                if (heading.Width > 0) TextRenderer.DrawText(e.Graphics, "Actions", grid.ColumnHeadersDefaultCellStyle.Font, heading, ModernTheme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            };
            grid.AccessibleName = "Selected applications"; return grid;
        }

        private void FillApplicationGrid(DataGridView grid, bool work, string query)
        {
            IEnumerable<GameDetectionTarget> targets = work ? workAppsList.Items.Cast<object>().Select(item => new GameDetectionTarget(item.ToString(), true)) : foldersList.Items.Cast<object>().OfType<GameDetectionTarget>();
            GameDetectionTarget[] filtered = targets.Where(item => string.IsNullOrEmpty(query) || item.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            string fingerprint = string.Join("|", filtered.Select(item => item.Path).ToArray());
            if (Convert.ToString(grid.Tag) == fingerprint && grid.Rows.Count == filtered.Length) { UpdateApplicationStatuses(grid, work); return; }
            grid.Tag = fingerprint; grid.Rows.Clear();
            foreach (GameDetectionTarget target in filtered)
            {
                string name = target.IsApplication ? Path.GetFileNameWithoutExtension(target.Path) : new DirectoryInfo(target.Path).Name;
                if (target.IsApplication && File.Exists(target.Path)) { try { string description = FileVersionInfo.GetVersionInfo(target.Path).FileDescription; if (!string.IsNullOrWhiteSpace(description)) name = description; } catch { } }
                Image icon;
                if (!applicationIcons.TryGetValue(target.Path, out icon))
                {
                    try { using (Icon extracted = target.IsApplication && File.Exists(target.Path) ? Icon.ExtractAssociatedIcon(target.Path) : null) icon = extracted == null ? SystemIcons.Application.ToBitmap() : extracted.ToBitmap(); }
                    catch { icon = SystemIcons.Application.ToBitmap(); }
                    applicationIcons[target.Path] = icon;
                }
                int index = grid.Rows.Add(icon, name, target.Path, target.IsApplication ? (File.Exists(target.Path) ? "Selected" : "Unavailable") : "Library folder"); grid.Rows[index].Tag = target;
            }
            if (filtered.Length == 0) grid.AccessibleDescription = "No applications. Use Add Application or Browse executable to choose one.";
            UpdateApplicationStatuses(grid, work);
        }

        private void UpdateApplicationStatuses(DataGridView grid, bool work)
        {
            string detected = work ? workAppsValue.Text : gameValue.Text;
            foreach (DataGridViewRow row in grid.Rows)
            {
                GameDetectionTarget target = row.Tag as GameDetectionTarget; if (target == null) continue;
                string name = Path.GetFileNameWithoutExtension(target.Path);
                bool running = target.IsApplication && detected.Split(',').Any(value => string.Equals(value.Trim(), name, StringComparison.OrdinalIgnoreCase));
                row.Cells[3].Value = !target.IsApplication ? "Library folder" : !File.Exists(target.Path) ? "Unavailable" : running ? "Detected" : "Selected";
                row.Cells[3].Style.ForeColor = running ? ModernTheme.Success : ModernTheme.Secondary;
            }
        }

        private void ApplicationAction(DataGridView grid, DataGridViewCellEventArgs e, bool work)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 4) return; GameDetectionTarget target = grid.Rows[e.RowIndex].Tag as GameDetectionTarget; if (target == null) return;
            if (e.ColumnIndex == 6)
            {
                if (ModernAlertForm.Show(this, "Remove application", "Remove " + Path.GetFileName(target.Path) + " from the selected applications? The executable will remain on your computer.", MessageBoxButtons.YesNo, MessageBoxIcon.Question, "Remove", "Keep") != DialogResult.Yes) return;
                if (work) { for (int i = workAppsList.Items.Count - 1; i >= 0; i--) if (string.Equals(workAppsList.Items[i].ToString(), target.Path, StringComparison.OrdinalIgnoreCase)) workAppsList.Items.RemoveAt(i); }
                else { for (int i = foldersList.Items.Count - 1; i >= 0; i--) { GameDetectionTarget item = foldersList.Items[i] as GameDetectionTarget; if (item != null && string.Equals(item.Path, target.Path, StringComparison.OrdinalIgnoreCase)) foldersList.Items.RemoveAt(i); } }
                MarkApplicationsChanged(); return;
            }
            string path = e.ColumnIndex == 5 && target.IsApplication ? Path.GetDirectoryName(target.Path) : target.Path;
            if ((!File.Exists(path) && !Directory.Exists(path)) || (e.ColumnIndex == 4 && !target.IsApplication)) { RetroMessage("The selected executable is unavailable. Choose an executable in Applications.", "Application unavailable"); return; }
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = target.IsApplication ? Path.GetDirectoryName(target.Path) : target.Path }); } catch (Exception ex) { ShowError(ex.Message); }
        }

        private void MarkApplicationsChanged()
        {
            string[] selectedWork = workAppsList.Items.Cast<object>().Select(item => item.ToString()).ToArray();
            GameDetectionTarget[] selectedGames = foldersList.Items.Cast<object>().OfType<GameDetectionTarget>().ToArray();
            selectionsDirty = !selectedWork.SequenceEqual(settings.workApps, StringComparer.OrdinalIgnoreCase) ||
                !selectedGames.Where(item => item.IsApplication).Select(item => item.Path).SequenceEqual(settings.gameApps, StringComparer.OrdinalIgnoreCase) ||
                !selectedGames.Where(item => !item.IsApplication).Select(item => item.Path).SequenceEqual(settings.gameFolders, StringComparer.OrdinalIgnoreCase);
            SetFooter(selectionsDirty ? "Application selection changed. Save selections to apply it." : "Application selections unchanged."); RefreshModernView();
        }
        private void RefreshApplicationPage()
        {
            if (managementGrid == null) return; ((ModernButton)workTab).Primary = applicationWorkTab; ((ModernButton)gameTab).Primary = !applicationWorkTab;
            workTab.Invalidate(); gameTab.Invalidate(); FillApplicationGrid(managementGrid, applicationWorkTab, applicationSearch.Text.Trim());
            applicationCount.Text = managementGrid.Rows.Count + " applications";
        }

        private Panel BuildTimersPage()
        {
            Panel content; Panel page = ScrollPage("Timer Settings", "Independent timers preserve their phase and remaining time when you switch modes.", out content);
            ModernCard workCard = new ModernCard { Location = new Point(0, 88), Size = new Size(520, 280) };
            ModernCard gameCard = new ModernCard { Location = new Point(536, 88), Size = new Size(520, 280) };
            content.Controls.Add(workCard); content.Controls.Add(gameCard);
            workCard.Controls.Add(At(ModernTheme.Label("Work timer", 13, true), 24, 24)); gameCard.Controls.Add(At(ModernTheme.Label("Game timer", 13, true), 24, 24));
            workTimerMinutesInput = DurationRow(workCard, "Work duration", 80); workBreakMinutesInput = DurationRow(workCard, "Break duration", 144);
            timerMinutesInput = DurationRow(gameCard, "Game duration", 80); breakMinutesInput = DurationRow(gameCard, "Break duration", 144);
            workTimerEnabledCheck = Check(workCard, "Enable work timer", 24, 224); timerEnabledCheck = Check(gameCard, "Enable game timer", 24, 224);
            timerEnabledCheck.CheckedChanged += delegate { timerMinutesInput.Enabled = breakMinutesInput.Enabled = timerEnabledCheck.Checked; UpdateTimerConfirmState(); };
            workTimerEnabledCheck.CheckedChanged += delegate { workTimerMinutesInput.Enabled = workBreakMinutesInput.Enabled = workTimerEnabledCheck.Checked; UpdateTimerConfirmState(); };
            workTimerMinutesInput.ValueChanged += delegate { UpdateTimerConfirmState(); }; workBreakMinutesInput.ValueChanged += delegate { UpdateTimerConfirmState(); };
            timerMinutesInput.ValueChanged += delegate { UpdateTimerConfirmState(); }; breakMinutesInput.ValueChanged += delegate { UpdateTimerConfirmState(); };
            ModernCard behavior = new ModernCard { Location = new Point(0, 384), Size = new Size(1056, 216) }; content.Controls.Add(behavior);
            behavior.Controls.Add(At(ModernTheme.Label("Behavior and notifications", 12, true), 24, 24));
            automaticModeCheck = Check(behavior, "Automatically follow the assigned foreground application", 24, 72); automaticModeCheck.CheckedChanged += delegate { workAutomaticModeCheck.Checked = automaticModeCheck.Checked; UpdateTimerConfirmState(); };
            pauseWithEscapeCheck = Check(behavior, "Press Escape in the focused game at break transitions", 24, 112);
            pauseWithEscapeCheck.CheckedChanged += delegate { UpdateTimerConfirmState(); };
            timerPopupOption = Check(behavior, "Show time-up alerts", 24, 160);
            timerSoundOption = Check(behavior, "Play timer alarm sound", 256, 160);
            timerPopupOption.CheckedChanged += delegate { if (popupOption != null) popupOption.Checked = timerPopupOption.Checked; };
            timerSoundOption.CheckedChanged += delegate { if (soundOption != null) soundOption.Checked = timerSoundOption.Checked; };
            Button notifications = ModernTheme.Button("Notification options", delegate { Navigate("Options"); }); notifications.Location = new Point(620, 152); behavior.Controls.Add(notifications);
            FlowLayoutPanel actions = new FlowLayoutPanel { Location = new Point(0, 624), Height = 56, Width = 700, WrapContents = false };
            actions.Controls.Add(ModernTheme.Button("Discard edits", delegate { LoadSettings(); SyncOptions(); }));
            actions.Controls.Add(ModernTheme.Button("Restore defaults", RestoreTimerDefaults));
            confirmTimerButton = ModernTheme.Button("Save timers", SaveAllSettings, true); actions.Controls.Add(confirmTimerButton);
            content.Controls.Add(actions);
            FlowLayoutPanel resets = new FlowLayoutPanel { Location = new Point(0, 688), Height = 48, Width = 480, WrapContents = false };
            resets.Controls.Add(ModernTheme.Button("Reset work timer", delegate { if (!PreviewMode) OpenTimerReset("work"); }));
            resets.Controls.Add(ModernTheme.Button("Reset game timer", delegate { if (!PreviewMode) OpenTimerReset("game"); })); content.Controls.Add(resets);
            page.Resize += delegate { int half = (content.Width - Ui(16)) / 2; workCard.Width = half; gameCard.Left = half + Ui(16); gameCard.Width = half; behavior.Width = content.Width; notifications.Left = Math.Max(Ui(480), behavior.Width - Ui(188)); };
            return page;
        }

        private DurationInput DurationRow(Control card, string title, int y)
        {
            card.Controls.Add(At(ModernTheme.Label(title, 9, true), 24, y)); DurationInput input = new DurationInput { Location = new Point(24, y + 26) }; card.Controls.Add(input); input.AccessibleName = title; return input;
        }
        private void RestoreTimerDefaults()
        {
            timerMinutesInput.TotalSeconds = workTimerMinutesInput.TotalSeconds = 1800;
            breakMinutesInput.TotalSeconds = workBreakMinutesInput.TotalSeconds = 300;
            timerEnabledCheck.Checked = workTimerEnabledCheck.Checked = true;
            pauseWithEscapeCheck.Checked = true; automaticModeCheck.Checked = false;
            UpdateTimerConfirmState(); SetFooter("Default timer settings loaded. Save timers to apply them.");
        }
        private static Control At(Control control, int x, int y) { control.Location = new Point(x, y); return control; }
        private CheckBox Check(Control parent, string text, int x, int y)
        {
            CheckBox check = new CheckBox { Text = text, Font = ModernTheme.Font(9), ForeColor = ModernTheme.Text, AutoSize = true, Location = new Point(x, y), AccessibleName = text }; parent.Controls.Add(check); return check;
        }

        private Panel BuildOptionsPage()
        {
            Panel content; Panel page = ScrollPage("Options", "Application preferences, notifications, detection, and stored data.", out content); content.Height = 1080;
            ModernCard general = new ModernCard { Location = new Point(0, 88), Height = 144 }; content.Controls.Add(general);
            general.Controls.Add(At(ModernTheme.Label("General", 12, true), 24, 20)); startupCheck = Check(general, "Start Game Management when I sign in", 24, 64);
            startupCheck.CheckedChanged += delegate { if (startupCheck.Focused && !PreviewMode) SetStartup(startupCheck.Checked); };
            trayCheck = Check(general, "Keep running in the notification area when the window closes", 24, 100); trayCheck.Checked = true;
            ModernCard notifications = new ModernCard { Location = new Point(0, 248), Height = 172 }; content.Controls.Add(notifications);
            notifications.Controls.Add(At(ModernTheme.Label("Notifications", 12, true), 24, 20));
            popupOption = Check(notifications, "Show time-up alerts", 24, 64); soundOption = Check(notifications, "Play timer alarm sound", 24, 100); summaryOption = Check(notifications, "Show end-of-session summary", 440, 64);
            popupOption.CheckedChanged += delegate { timerPopupOption.Checked = popupOption.Checked; UpdateTimerConfirmState(); };
            soundOption.CheckedChanged += delegate { timerSoundOption.Checked = soundOption.Checked; UpdateTimerConfirmState(); };
            ModernCard detection = new ModernCard { Location = new Point(0, 436), Height = 152 }; content.Controls.Add(detection);
            detection.Controls.Add(At(ModernTheme.Label("Automatic detection and performance", 12, true), 24, 20));
            detection.Controls.Add(At(ModernTheme.Label("Scan interval (seconds)", 9), 24, 64)); pollInput = new NumericUpDown { Minimum = 2, Maximum = 60, Value = 3, Location = new Point(220, 62), Width = 80, AccessibleName = "Scan interval seconds" }; detection.Controls.Add(pollInput);
            detection.Controls.Add(At(ModernTheme.Label("Game brightness (%)", 9), 24, 104)); brightnessInput = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 75, Location = new Point(220, 102), Width = 80, AccessibleName = "Game brightness percent" }; detection.Controls.Add(brightnessInput);
            Label detectionNote = ModernTheme.Label("Foreground mode switching is configured in Timer Settings.", 9); detectionNote.AutoSize = false; detectionNote.Location = new Point(440, 64); detection.Controls.Add(detectionNote);
            ModernCard storage = new ModernCard { Location = new Point(0, 604), Height = 132 }; content.Controls.Add(storage);
            storage.Controls.Add(At(ModernTheme.Label("Data and storage", 12, true), 24, 20)); storage.Controls.Add(At(ModernTheme.Label("Settings and activity are kept in your existing GameManagement folder.", 9), 24, 52));
            Button backup = ModernTheme.Button("Export backup", ExportPortableBackup); backup.Location = new Point(24, 80); storage.Controls.Add(backup);
            Button restore = ModernTheme.Button("Restore backup", delegate { if (!PreviewMode) ImportPortableBackup(); }); restore.Location = new Point(176, 80); storage.Controls.Add(restore);
            ModernCard advanced = new ModernCard { Location = new Point(0, 752), Height = 232 }; content.Controls.Add(advanced);
            advanced.Controls.Add(At(ModernTheme.Label("Advanced and appearance", 12, true), 24, 20));
            Button activity = ModernTheme.Button("Recent activity", delegate { activityVisible = true; RefreshStatus(); logGroup.Visible = !logGroup.Visible; }); activity.Location = new Point(24, 64); advanced.Controls.Add(activity); toggleLogButton = activity;
            Button diagnostics = ModernTheme.Button("Diagnostics", OpenDiagnostics); diagnostics.Location = new Point(176, 64); advanced.Controls.Add(diagnostics);
            Button flowers = ModernTheme.Button("Pixel gerberas", delegate
            {
                using (Form view = new Form { Text = "Pixel gerberas", Size = new Size(560, 360), BackColor = ModernTheme.Background, StartPosition = FormStartPosition.CenterParent })
                { PixelGerberaPanel art = new PixelGerberaPanel { Dock = DockStyle.Fill }; view.Controls.Add(art); view.ShowDialog(this); }
            }); flowers.Location = new Point(328, 64); advanced.Controls.Add(flowers);
            logGroup = new GroupBox { Text = "Recent activity", Location = new Point(24, 112), Height = 104, Visible = false }; advanced.Controls.Add(logGroup);
            logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = ModernTheme.Surface, Font = ModernTheme.Font(8) }; logGroup.Controls.Add(logBox);
            Button save = ModernTheme.Button("Save options", SaveAllSettings, true); save.Location = new Point(0, 1008); content.Controls.Add(save);
            page.Resize += delegate { foreach (ModernCard card in new[] { general, notifications, detection, storage, advanced }) card.Width = content.Width; logGroup.Width = advanced.Width - Ui(48); detectionNote.Size = new Size(Math.Max(Ui(180), content.Width - Ui(464)), Ui(70)); };
            return page;
        }

        private void SyncOptions() { popupOption.Checked = timerPopupEnabled; soundOption.Checked = timerSoundEnabled; summaryOption.Checked = sessionSummaryEnabled; }
        private bool HasUnsavedSettings()
        {
            return settings != null && (selectionsDirty || confirmTimerButton.Enabled ||
                popupOption.Checked != timerPopupEnabled || soundOption.Checked != timerSoundEnabled || summaryOption.Checked != sessionSummaryEnabled ||
                (int)pollInput.Value != settings.pollSeconds || (int)brightnessInput.Value != settings.gameBrightnessPercent);
        }
        private bool ConfirmExitEdits()
        {
            if (PreviewMode || !HasUnsavedSettings()) return true;
            DialogResult choice = ModernAlertForm.Show(this, "Unsaved changes", "Save your pending settings before exiting?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, "Save", "Discard", "Cancel");
            if (choice == DialogResult.No) return true;
            if (choice == DialogResult.Yes)
            {
                SaveAllSettings(ExitApplication);
            }
            return false;
        }
        private void SaveAllSettings() { SaveAllSettings(null); }
        private void SaveAllSettings(Action afterSave)
        {
            if (PreviewMode) { SetFooter("Preview is isolated. The installed settings are unchanged."); return; }
            bool oldPopup = timerPopupEnabled, oldSound = timerSoundEnabled, oldSummary = sessionSummaryEnabled;
            timerPopupEnabled = popupOption.Checked; timerSoundEnabled = soundOption.Checked; sessionSummaryEnabled = summaryOption.Checked;
            SaveSettingsAsync(null, delegate(bool saved) { if (saved) { selectionsDirty = false; SetFooter("Settings and selections saved."); if (afterSave != null) afterSave(); } else { timerPopupEnabled = oldPopup; timerSoundEnabled = oldSound; sessionSummaryEnabled = oldSummary; SyncOptions(); } });
        }
        private void SelectMode(bool work)
        {
            if (work == workView) return;
            if (PreviewMode) { ShowManagementMode(work); RefreshModernView(); return; }
            if (HasUnsavedSettings()) { RetroMessage("Save or discard your pending settings before switching modes.", "Unsaved changes"); Navigate(selectionsDirty ? "Applications" : "Timer Settings"); return; }
            if (lastAssignedForegroundPath != null) manualOverridePath = lastAssignedForegroundPath;
            RequestManagementMode(work ? "work" : "game", false);
        }
        private void Navigate(string page)
        {
            currentPage = page;
            foreach (KeyValuePair<string, Button> item in navigation) { ((DockButton)item.Value).Selected = item.Key == page; item.Value.Invalidate(); }
            homePage.Visible = page == "Home"; applicationsPage.Visible = page == "Applications"; timersPage.Visible = page == "Timer Settings"; optionsPage.Visible = page == "Options"; reportsPage.Visible = page == "Reports";
            Control selected = page == "Home" ? homePage : page == "Applications" ? applicationsPage : page == "Timer Settings" ? timersPage : page == "Options" ? optionsPage : (Control)reportsPage;
            selected.BringToFront(); if (page == "Applications") RefreshApplicationPage(); if (page == "Reports") reportsPage.RefreshData();
        }
        internal void SetPageForPreview(string page) { Navigate(page); }
        internal void SetOverlayForPreview(string name) { overlays.ShowForPreview(name); }
        internal void DrawOverlayForPreview(Graphics graphics) { if (currentPage == "Home") overlays.DrawPreviewOverlay(graphics, this); }
        private void ToggleMaximize() { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }
        private void StartModernViewRefresh()
        {
            if (modernViewClosing || IsDisposed || Disposing) return;
            modernViewReady = true;
            if (!PreviewMode) modernRefresh.Start();
        }
        private void StopModernViewRefresh()
        {
            modernViewClosing = true;
            modernViewReady = false;
            modernRefresh.Stop();
        }
        private void DisposeModernViewResources()
        {
            if (modernViewResourcesDisposed) return;
            StopModernViewRefresh();
            modernViewResourcesDisposed = true;
            modernRefresh.Dispose();
            if (overlays != null) overlays.Dispose();
            foreach (Image image in applicationIcons.Values) image.Dispose();
            applicationIcons.Clear();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeModernViewResources();
            base.Dispose(disposing);
        }
        internal void RunModernRefreshForTest() { RefreshModernView(); }
        internal bool ModernRefreshEnabledForTest { get { return !modernViewResourcesDisposed && modernRefresh.Enabled; } }

        private static bool ModernControlsAvailable(params Control[] controls)
        {
            return controls.All(control => control != null && !control.IsDisposed && !control.Disposing);
        }
        private static bool ModernControlAvailable<T>(Dictionary<bool, T> controls, bool work) where T : Control
        {
            T control;
            return controls.TryGetValue(work, out control) && ModernControlsAvailable(control);
        }
        private string ModernHomeBindingIssue()
        {
            if (!ModernControlsAvailable(gameDashboardGrid, workDashboardGrid, foldersList, workAppsList)) return "selected applications";
            if (!ModernControlsAvailable(gameElapsedValue, gameProgress, workProgress, minutesLeftValue, timerValue, workMinutesLeftValue, workTimerPhaseValue)) return "session timer";
            if (!ModernControlsAvailable(statusValue, workModeValue, gpuTemperatureValue, workGpuValue, gameValue, workAppsValue)) return "session status";
            foreach (bool work in new[] { false, true })
            {
                if (!ModernControlAvailable(displayPhases, work) || !ModernControlAvailable(modeBadges, work)) return "mode status";
                if (!ModernControlAvailable(thermalBadges, work) || !ModernControlAvailable(gpuDisplays, work)) return "GPU temperature card";
                if (!ModernControlAvailable(dashboardResumeButtons, work) || !ModernControlAvailable(dashboardPauseButtons, work) || !ModernControlAvailable(dashboardResetButtons, work)) return "timer actions";
                Control gpuCard = gpuDisplays[work].Parent;
                if (!ModernControlsAvailable(gpuCard)) return "GPU temperature card";
                if (!ModernControlAvailable(dashboardSettings, work) || !ModernControlAvailable(dashboardApplications, work) || !ModernControlAvailable(sessionOverviews, work)) return "session overview";
            }
            return null;
        }
        private void UpdateDashboardActionState(bool canResume, bool canPause, bool canReset)
        {
            foreach (bool work in new[] { false, true })
            {
                Button resume, pause, reset;
                if (dashboardResumeButtons.TryGetValue(work, out resume) && ModernControlsAvailable(resume)) resume.Enabled = canResume;
                if (dashboardPauseButtons.TryGetValue(work, out pause) && ModernControlsAvailable(pause)) pause.Enabled = canPause;
                if (dashboardResetButtons.TryGetValue(work, out reset) && ModernControlsAvailable(reset)) reset.Enabled = canReset;
            }
        }
        private void UpdateDashboardActionState()
        {
            if (!ModernControlsAvailable(enableButton, pauseButton)) return;
            Button gameReset;
            bool canReset = !dashboardResetButtons.TryGetValue(false, out gameReset) || !ModernControlsAvailable(gameReset) || gameReset.Enabled;
            UpdateDashboardActionState(enableButton.Enabled, pauseButton.Enabled, canReset);
        }
        private void RefreshModernView()
        {
            if (!modernViewReady || modernViewClosing || exiting || IsDisposed || Disposing || settings == null) return;
            if (currentPage == "Home")
            {
                string bindingIssue = ModernHomeBindingIssue();
                if (bindingIssue != null)
                {
                    string warning = "Dashboard refresh paused: " + bindingIssue + " is unavailable.";
                    if (modernViewBindingWarning != warning && ModernControlsAvailable(footerLabel)) SetFooter(warning);
                    modernViewBindingWarning = warning;
                    return;
                }
                if (modernViewBindingWarning != null)
                {
                    modernViewBindingWarning = null;
                    if (ModernControlsAvailable(footerLabel)) SetFooter("Dashboard refresh resumed.");
                }
                FillApplicationGrid(workDashboardGrid, true, null); FillApplicationGrid(gameDashboardGrid, false, null);
                SessionClock gameClock = SessionClock.Load(rootPath, "game", json);
                TimeSpan elapsed = gameClock.ElapsedAt(DateTime.Now);
                gameElapsedValue.Text = string.Format("{0:00}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds);
                UpdateProgress(gameProgress, minutesLeftValue.Text, timerValue.Text, TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30), TimerDuration.Resolve(settings.breakTimerSeconds, settings.breakTimerMinutes, 5));
                UpdateProgress(workProgress, workMinutesLeftValue.Text, workTimerPhaseValue.Text, TimerDuration.Resolve(settings.workTimerSeconds, settings.workTimerMinutes, 30), TimerDuration.Resolve(settings.workBreakSeconds, settings.workBreakMinutes, 5));
                foreach (bool work in new[] { false, true })
                {
                    string phase = work ? workTimerPhaseValue.Text : timerValue.Text;
                    displayPhases[work].Text = (work ? "WORK TIMER" : "GAME TIMER") + " · " + (phase.IndexOf("break", StringComparison.OrdinalIgnoreCase) >= 0 ? "Break" : work ? "Focus session" : "Play session") + (phase.IndexOf("paused", StringComparison.OrdinalIgnoreCase) >= 0 ? " · Paused" : phase == "Off" ? " · Disabled" : "");
                    string status = work ? workModeValue.Text : statusValue.Text;
                    bool active = status.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool paused = status.IndexOf("paused", StringComparison.OrdinalIgnoreCase) >= 0 || phase.IndexOf("paused", StringComparison.OrdinalIgnoreCase) >= 0;
                    modeBadges[work].Text = (work ? "Work" : "Game") + (paused ? " paused" : active ? " active" : " ready");
                    modeBadges[work].Accent = paused ? ModernTheme.Secondary : active ? ModernTheme.Success : work ? ModernTheme.Work : ModernTheme.Game;
                    UpdateDashboardActionState();
                    string gpu = work ? workGpuValue.Text : gpuTemperatureValue.Text;
                    string thermal = gpu.Contains("Danger") ? "Hot" : gpu.Contains("Warm") ? "Warm" : gpu.Contains("Safe") ? "Safe" : "Unavailable";
                    int conditionAt = gpu.LastIndexOf("  ", StringComparison.Ordinal);
                    gpuDisplays[work].Text = conditionAt > 0 ? gpu.Substring(0, conditionAt).Replace(" | ", " / ") : gpu;
                    gpuDisplays[work].AccessibleDescription = gpu;
                    thermalBadges[work].Text = thermal; thermalBadges[work].Accent = thermal == "Hot" ? ModernTheme.Error : thermal == "Warm" ? ModernTheme.Warning : thermal == "Safe" ? ModernTheme.Success : ModernTheme.Secondary;
                    thermalBadges[work].Visible = thermal != "Unavailable";
                    Control gpuCard = gpuDisplays[work].Parent;
                    if (ModernControlsAvailable(gpuCard)) gpuCard.PerformLayout();
                    int duration = work ? TimerDuration.Resolve(settings.workTimerSeconds, settings.workTimerMinutes, 30) : TimerDuration.Resolve(settings.gameTimerSeconds, settings.gameTimerMinutes, 30);
                    int pause = work ? TimerDuration.Resolve(settings.workBreakSeconds, settings.workBreakMinutes, 5) : TimerDuration.Resolve(settings.breakTimerSeconds, settings.breakTimerMinutes, 5);
                    bool enabled = work ? settings.workTimerEnabled : settings.gameTimerEnabled;
                    dashboardSettings[work].SetValues(work ? "Work duration" : "Game duration", TimerDuration.Format(duration), TimerDuration.Format(pause), enabled,
                        settings.automaticModeDetection, settings.timerSoundEnabled, settings.timerPopupEnabled);
                    dashboardApplications[work].Text = (work ? workAppsList.Items.Count : foldersList.Items.Count) + " applications";
                    if (work == workView) sessionOverviews[work].RefreshData(PreviewMode);
                }
            }
            if (currentPage == "Applications") RefreshApplicationPage();
        }
        private void UpdateProgress(ModernProgress progress, string time, string phase, int duration, int breakDuration)
        {
            TimeSpan left; if (time.Count(c => c == ':') == 1) time = "00:" + time;
            progress.Value = TimeSpan.TryParse(time, out left) ? 1 - left.TotalSeconds / Math.Max(1, phase.IndexOf("break", StringComparison.OrdinalIgnoreCase) >= 0 ? breakDuration : duration) : 0; progress.Invalidate();
        }
        protected override void WndProc(ref Message message)
        {
            const int hitTest = 0x84;
            if (message.Msg == hitTest && WindowState != FormWindowState.Maximized)
            {
                base.WndProc(ref message); Point p = PointToClient(new Point((int)message.LParam)); int edge = 7;
                bool left = p.X < edge, right = p.X >= Width - edge, top = p.Y < edge, bottom = p.Y >= Height - edge;
                if (top && left) message.Result = (IntPtr)13; else if (top && right) message.Result = (IntPtr)14;
                else if (bottom && left) message.Result = (IntPtr)16; else if (bottom && right) message.Result = (IntPtr)17;
                else if (left) message.Result = (IntPtr)10; else if (right) message.Result = (IntPtr)11;
                else if (top) message.Result = (IntPtr)12; else if (bottom) message.Result = (IntPtr)15; return;
            }
            base.WndProc(ref message);
        }
    }
    internal sealed class DockButton : Button
    {
        internal string Glyph;
        private bool selected, hover;
        internal bool Selected { get { return selected; } set { if (selected == value) return; selected = value; Animate(); } }
        private readonly Timer transition = new Timer { Interval = 15 };
        private double amount, from, target;
        private DateTime started;
        internal DockButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = ModernTheme.Background; Cursor = Cursors.Hand;
            Font = ModernTheme.Font(9); SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseEnter += delegate { hover = true; Animate(); }; MouseLeave += delegate { hover = false; Animate(); };
            transition.Tick += delegate
            {
                double progress = Math.Min(1, (DateTime.UtcNow - started).TotalMilliseconds / 170);
                amount = from + (target - from) * (1 - Math.Pow(1 - progress, 3));
                Invalidate(); if (progress >= 1) transition.Stop();
            };
        }
        private void Animate()
        {
            target = selected ? 1 : hover ? .55 : 0;
            transition.Stop();
            if (ModernTheme.ReducedMotion || !Visible) { amount = target; Invalidate(); return; }
            from = amount; started = DateTime.UtcNow; transition.Start();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End) && Parent != null)
            {
                DockButton[] buttons = Parent.Controls.OfType<DockButton>().OrderBy(button => button.Left).ToArray();
                int index = Array.IndexOf(buttons, this), next = e.KeyCode == Keys.Home ? 0 : e.KeyCode == Keys.End ? buttons.Length - 1 : (index + (e.KeyCode == Keys.Right ? 1 : -1) + buttons.Length) % buttons.Length;
                buttons[next].Focus(); buttons[next].PerformClick(); e.Handled = true; e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color color = Selected ? ModernTheme.Blue : ModernTheme.Secondary;
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (System.Drawing.Drawing2D.GraphicsPath shape = ModernTheme.Rounded(new Rectangle(2, 0, Width - 5, Height - 1), ModernTheme.Px(this, ModernTheme.Radius)))
            using (Brush fill = new SolidBrush(ModernTheme.Tint(ModernTheme.Blue, .09 * amount))) e.Graphics.FillPath(fill, shape);
            int iconSize = ModernTheme.Px(this, 16), labelHeight = ModernTheme.Px(this, 17);
            ModernTheme.DrawIcon(e.Graphics, Glyph, new Rectangle((Width - iconSize) / 2, ModernTheme.Px(this, 4), iconSize, iconSize), color);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, Height - ModernTheme.Px(this, 20), Width, labelHeight), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Selected) using (Pen pen = new Pen(ModernTheme.Blue, 2)) e.Graphics.DrawLine(pen, ModernTheme.Px(this, 24), Height - 2, Width - ModernTheme.Px(this, 24), Height - 2);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Width - 8, Height - 8));
        }
        protected override void Dispose(bool disposing) { if (disposing) transition.Dispose(); base.Dispose(disposing); }
    }
}
