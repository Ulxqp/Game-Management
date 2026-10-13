using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GameManagement
{
    internal sealed partial class MainForm
    {
        private Panel Dashboard(bool work)
        {
            Color accent = work ? ModernTheme.Work : ModernTheme.Game;
            Panel surface = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ModernTheme.Background, AccessibleName = work ? "Work dashboard" : "Game dashboard" };
            Panel content = new Panel { Location = new Point(64, 16), Size = new Size(1170, 784), BackColor = ModernTheme.Background };
            surface.Controls.Add(content);
            ModernButton workMode = (ModernButton)ModernTheme.Button("Work Mode", delegate { SelectMode(true); }, work);
            ModernButton gameMode = (ModernButton)ModernTheme.Button("Game Mode", delegate { SelectMode(false); }, !work);
            workMode.Glyph = "\uE821"; gameMode.Glyph = "\uE7FC"; workMode.Accent = ModernTheme.Work; gameMode.Accent = ModernTheme.Game;
            workMode.Font = gameMode.Font = ModernTheme.Font(10, FontStyle.Bold);
            workMode.Bounds = new Rectangle(0, 0, 184, 48); gameMode.Bounds = new Rectangle(192, 0, 184, 48);
            content.Controls.Add(workMode); content.Controls.Add(gameMode);
            if (work) workModeFlipButton = gameMode; else modeFlipButton = workMode;
            StatusBadge modeBadge = new StatusBadge { Text = work ? "Work ready" : "Game ready", Accent = accent, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "Current management status" };
            modeBadges.Add(work, modeBadge); content.Controls.Add(modeBadge);
            if (work) workModeValue = ModernTheme.Label("Ready"); else statusValue = ModernTheme.Label("Ready");

            IconStatCard elapsedCard = new IconStatCard("Session time", "\uE917", accent);
            IconStatCard cyclesCard = new IconStatCard("Completed cycles", "\uE895", accent);
            elapsedCard.ValueLabel.Text = "00:00:00"; cyclesCard.ValueLabel.Text = "0";
            if (work) { workElapsedValue = elapsedCard.ValueLabel; workCycleValue = cyclesCard.ValueLabel; }
            else { gameElapsedValue = elapsedCard.ValueLabel; cycleValue = cyclesCard.ValueLabel; }
            ModernCard hardware = new ModernCard();
            ModernIcon thermalIcon = new ModernIcon { Glyph = "thermometer", Accent = ModernTheme.Success, Circle = true, Size = new Size(44, 44) }; hardware.Controls.Add(thermalIcon);
            Label thermalTitle = ModernTheme.Label("GPU temperature / peak", 8.5F); thermalTitle.AutoSize = false; thermalTitle.ForeColor = ModernTheme.Secondary; hardware.Controls.Add(thermalTitle);
            Label gpu = ModernTheme.Label("Unavailable", 15, true); gpu.AutoSize = false; gpu.AutoEllipsis = true; hardware.Controls.Add(gpu);
            gpuDisplays.Add(work, gpu); Label gpuSource = ModernTheme.Label("Unavailable");
            Label cpu = ModernTheme.Label("Unavailable", 8); cpu.AutoSize = false; cpu.AutoEllipsis = true; cpu.ForeColor = ModernTheme.Secondary; hardware.Controls.Add(cpu);
            Label cpuTitle = ModernTheme.Label("CPU", 8, true); cpuTitle.AutoSize = false; cpuTitle.ForeColor = ModernTheme.TextFaint; hardware.Controls.Add(cpuTitle);
            StatusBadge thermal = new StatusBadge { Text = "Unavailable", Accent = ModernTheme.Secondary, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, AccessibleName = "GPU temperature safety status" }; hardware.Controls.Add(thermal); thermalBadges.Add(work, thermal);
            if (work) { workCpuValue = cpu; workGpuValue = gpuSource; } else { cpuTemperatureValue = cpu; gpuTemperatureValue = gpuSource; }
            hardware.Layout += delegate
            {
                bool compact = hardware.Width < Ui(300);
                thermalIcon.SetBounds(Ui(16), (hardware.Height - Ui(44)) / 2, Ui(44), Ui(44));
                int textX = Ui(72), rightInset = thermal.Visible && !compact ? Ui(108) : Ui(16);
                thermalTitle.SetBounds(textX, Ui(12), Math.Max(Ui(40), hardware.Width - textX - rightInset), Ui(18));
                gpu.SetBounds(textX, Ui(31), Math.Max(Ui(40), hardware.Width - textX - rightInset), Ui(28));
                cpuTitle.SetBounds(textX, Ui(60), Ui(32), Ui(18));
                cpu.SetBounds(textX + Ui(36), Ui(60), Math.Max(Ui(48), hardware.Width - textX - Ui(52)), Ui(18));
                thermal.SetBounds(hardware.Width - Ui(94), Ui(compact ? 52 : 28), Ui(82), Ui(28));
            };
            content.Controls.Add(elapsedCard); content.Controls.Add(cyclesCard); content.Controls.Add(hardware);

            ModernCard timer = new ModernCard { Location = new Point(0, 152), Height = 152, CornerRadius = ModernTheme.LargeRadius };
            Label phase = ModernTheme.Label("Ready"); // Existing refresh methods keep the authoritative phase here.
            Label phaseDisplay = ModernTheme.Label(work ? "WORK TIMER · Focus session" : "GAME TIMER · Play session", 9.5F);
            phaseDisplay.AutoSize = false; phaseDisplay.TextAlign = ContentAlignment.MiddleCenter; phaseDisplay.ForeColor = ModernTheme.Secondary; timer.Controls.Add(phaseDisplay); displayPhases.Add(work, phaseDisplay);
            Label countdown = ModernTheme.Label("--:--", 38, true); countdown.AutoSize = false; countdown.TextAlign = ContentAlignment.MiddleCenter; countdown.AccessibleName = work ? "Work time remaining" : "Game time remaining"; timer.Controls.Add(countdown);
            ModernProgress progress = new ModernProgress { Accent = accent }; timer.Controls.Add(progress);
            Button resume = ModernTheme.Button("Start / Resume", delegate { if (!PreviewMode) EnableGameManagement(); }, true); ((ModernButton)resume).Accent = accent;
            Button pause = ModernTheme.Button("Pause", delegate { if (!PreviewMode) PauseGameManagement(); });
            Button reset = ModernTheme.Button("Reset", delegate { if (!PreviewMode) OpenTimerReset(work ? "work" : "game"); });
            pause.Enabled = false;
            resume.Font = pause.Font = reset.Font = ModernTheme.Font(10, FontStyle.Bold);
            foreach (Button action in new[] { resume, pause, reset }) timer.Controls.Add(action);
            dashboardResumeButtons.Add(work, resume); dashboardPauseButtons.Add(work, pause); dashboardResetButtons.Add(work, reset);
            if (work) { workTimerPhaseValue = phase; workMinutesLeftValue = countdown; workProgress = progress; }
            else { timerValue = phase; minutesLeftValue = countdown; gameProgress = progress; enableButton = resume; pauseButton = pause; }
            timer.Resize += delegate
            {
                bool roomy = timer.Height >= Ui(200); int width = Math.Max(Ui(100), timer.Width - Ui(48));
                int actionHeight = Ui(42), actionTop = timer.Height - actionHeight - Ui(roomy ? 24 : 5);
                int progressTop = actionTop - Ui(roomy ? 22 : 14), phaseTop = Ui(roomy ? 20 : 8);
                int countdownTop = phaseTop + Ui(roomy ? 26 : 19), countdownHeight = Math.Max(1, progressTop - Ui(6) - countdownTop);
                phaseDisplay.SetBounds(Ui(24), phaseTop, width, Ui(24)); countdown.SetBounds(Ui(24), countdownTop, width, countdownHeight);
                FitCountdownFont(countdown, countdownHeight);
                progress.SetBounds(Ui(24), progressTop, width, Ui(8)); int left = Math.Max(Ui(16), (timer.Width - Ui(420)) / 2);
                resume.SetBounds(left, actionTop, Ui(132), actionHeight); pause.SetBounds(left + Ui(144), actionTop, Ui(132), actionHeight); reset.SetBounds(left + Ui(288), actionTop, Ui(132), actionHeight);
            };
            content.Controls.Add(timer);

            ModernCard apps = new ModernCard { Location = new Point(0, 316), Height = 160 };
            Control appGlyph = ModernTheme.Icon("\uE8A7", ModernTheme.Text, 18); appGlyph.Location = new Point(16, 15); apps.Controls.Add(appGlyph);
            Label appsTitle = ModernTheme.Label("Selected applications", 10, true); appsTitle.Location = new Point(44, 15); apps.Controls.Add(appsTitle);
            Label count = ModernTheme.Label("0 applications", 8); count.AutoSize = false; count.TextAlign = ContentAlignment.MiddleRight; count.ForeColor = ModernTheme.Secondary; apps.Controls.Add(count); dashboardApplications.Add(work, count);
            Button manage = ModernTheme.Button("Add Application…", delegate { applicationWorkTab = work; Navigate("Applications"); }); apps.Controls.Add(manage);
            DataGridView grid = AppGrid(); grid.Location = new Point(16, 52); grid.ColumnHeadersHeight = ModernTheme.CompactHeight; grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(12, 4, 12, 4); grid.RowTemplate.Height = 32; apps.Controls.Add(grid);
            Label empty = ModernTheme.Label("No applications selected. Add an application to begin.", 9); empty.AutoSize = false; empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = ModernTheme.Secondary; apps.Controls.Add(empty);
            grid.RowsAdded += delegate { empty.Visible = grid.Rows.Count == 0; }; grid.RowsRemoved += delegate { empty.Visible = grid.Rows.Count == 0; };
            if (work) { workDashboardGrid = grid; workAppsValue = ModernTheme.Label("None"); } else { gameDashboardGrid = grid; gameValue = ModernTheme.Label("None"); }
            grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e) { ApplicationAction(grid, e, work); };
            apps.Resize += delegate
            {
                bool roomy = apps.Height >= Ui(200); int headerHeight = Ui(roomy ? 36 : 32), rowHeight = Ui(roomy ? 56 : 32);
                manage.SetBounds(apps.Width - Ui(164), Ui(8), Ui(148), Ui(36)); count.SetBounds(apps.Width - Ui(300), Ui(12), Ui(124), Ui(28));
                grid.ColumnHeadersHeight = headerHeight; grid.RowTemplate.Height = rowHeight;
                foreach (DataGridViewRow row in grid.Rows) row.Height = rowHeight;
                grid.Size = new Size(Math.Max(Ui(200), apps.Width - Ui(32)), Math.Max(Ui(72), apps.Height - Ui(64)));
                empty.SetBounds(Ui(16), Ui(78), Math.Max(Ui(160), apps.Width - Ui(32)), Math.Max(Ui(32), apps.Height - Ui(94)));
            };
            content.Controls.Add(apps);

            ModernCard settingsCard = new ModernCard { Location = new Point(0, 488), Height = 296 };
            Control settingsGlyph = ModernTheme.Icon("\uE713", ModernTheme.Text, 18); settingsGlyph.Location = new Point(16, 16); settingsCard.Controls.Add(settingsGlyph);
            Label settingsTitle = ModernTheme.Label("Timer Settings", 10, true); settingsTitle.Location = new Point(44, 16); settingsCard.Controls.Add(settingsTitle);
            Button configure = ModernTheme.Button("Configure", delegate { Navigate("Timer Settings"); }); settingsCard.Controls.Add(configure);
            ModernTimerSettingsSummary summary = new ModernTimerSettingsSummary { Location = new Point(16, 58), Size = new Size(520, 210) }; settingsCard.Controls.Add(summary); dashboardSettings.Add(work, summary);
            settingsCard.Resize += delegate { configure.SetBounds(settingsCard.Width - Ui(120), Ui(8), Ui(104), Ui(36)); summary.Size = new Size(Math.Max(Ui(260), settingsCard.Width - Ui(32)), Math.Max(Ui(150), settingsCard.Height - Ui(74))); };
            content.Controls.Add(settingsCard);
            ModernSessionOverview overview = new ModernSessionOverview(historyPath) { Location = new Point(584, 488), Height = 296 };
            overview.ViewReportsRequested += delegate { reportsPage.SetDateRange(overview.RangeStart, overview.RangeEnd); Navigate("Reports"); };
            sessionOverviews.Add(work, overview); content.Controls.Add(overview);

            surface.Resize += delegate
            {
                int width = Math.Max(Ui(660), surface.ClientSize.Width - Ui(88)); content.Width = width;
                modeBadge.SetBounds(width - Ui(142), Ui(7), Ui(134), Ui(34));
                int gap = Ui(12), metricWidth = (width - 2 * gap) / 3;
                elapsedCard.SetBounds(0, Ui(56), metricWidth, Ui(84)); cyclesCard.SetBounds(metricWidth + gap, Ui(56), metricWidth, Ui(84)); hardware.SetBounds((metricWidth + gap) * 2, Ui(56), width - (metricWidth + gap) * 2, Ui(84));
                bool roomy = surface.ClientSize.Height >= Ui(930);
                timer.SetBounds(0, Ui(roomy ? 156 : 152), width, Ui(roomy ? 220 : 152));
                apps.SetBounds(0, timer.Bottom + Ui(roomy ? 16 : 12), width, Ui(roomy ? 216 : 160));
                int bottomTop = apps.Bottom + Ui(roomy ? 16 : 12), bottomGap = Ui(16), bottomHeight = Ui(296);
                timer.Width = apps.Width = width;
                bool narrow = width < Ui(940);
                int settingsWidth = narrow ? width : (width - bottomGap) * 20 / 43;
                settingsCard.SetBounds(0, bottomTop, settingsWidth, bottomHeight);
                overview.SetBounds(narrow ? 0 : settingsCard.Right + bottomGap, narrow ? settingsCard.Bottom + Ui(16) : bottomTop, narrow ? width : width - settingsCard.Right - bottomGap, bottomHeight);
                content.Height = overview.Bottom; surface.AutoScrollMinSize = new Size(0, content.Bottom + Ui(16));
            };
            return surface;
        }
        private static void FitCountdownFont(Label label, int availableHeight)
        {
            const float preferredSize = 38F, minimumSize = 28F;
            float size = preferredSize; Font fitted = null;
            while (size >= minimumSize)
            {
                Font candidate = ModernTheme.Font(size, FontStyle.Bold);
                int height = TextRenderer.MeasureText("88:88", candidate, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
                if (height <= availableHeight) { fitted = candidate; break; }
                candidate.Dispose(); size -= 1F;
            }
            if (fitted == null) { size = minimumSize; fitted = ModernTheme.Font(size, FontStyle.Bold); }
            if (Math.Abs(label.Font.SizeInPoints - size) < .1F) { fitted.Dispose(); return; }
            Font previous = label.Font; label.Font = fitted; previous.Dispose();
        }
    }
}
