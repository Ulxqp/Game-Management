using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GameManagement
{
    // These controls float over the dashboard. They never change its docked layout.
    internal sealed class OverlayPanels : IDisposable
    {
        private readonly Control host;
        private readonly Panel rail;
        private readonly RailButton calendarRail;
        private readonly RailButton tasksRail;
        private readonly ModernCard calendarPanel;
        private readonly ModernCard tasksPanel;
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer closeTimer = new Timer();
        private readonly Timer animationTimer = new Timer();
        private readonly OverlayTaskStore taskStore;
        private readonly OverlayPanelPreferenceStore panelPreferences;
        private readonly bool persistPins;
        private readonly Button calendarPin;
        private readonly Button tasksPin;
        private readonly Label calendarMonth;
        private readonly Label calendarSummary;
        private readonly Label calendarHint;
        private readonly Button viewReports;
        private readonly Label[] weekLabels = new Label[7];
        private readonly CalendarDayButton[] calendarDays = new CalendarDayButton[42];
        private readonly Button[] rangeButtons = new Button[4];
        private readonly DateTimePicker rangeFrom;
        private readonly DateTimePicker rangeTo;
        private readonly Label fromLabel;
        private readonly Label toLabel;
        private readonly Button previousMonth;
        private readonly Button nextMonth;
        private readonly TextBox taskTitle;
        private readonly ModernCard taskInputCard;
        private readonly Label taskInputHint;
        private readonly ComboBox taskCategory;
        private readonly Button taskAdd;
        private readonly Panel taskList;
        private readonly Label taskProgressLabel;
        private readonly Panel taskProgressTrack;
        private readonly Panel taskProgressFill;
        private readonly Label taskStatus;
        private readonly Button undoRemove;
        private readonly Button clearCompleted;
        private readonly Button[] categoryButtons = new Button[4];
        private readonly Label taskInputLabel;
        private readonly Label taskCategoryLabel;
        private DateTime month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        private DateTime rangeStart = DateTime.Today;
        private DateTime rangeEnd = DateTime.Today;
        private string rangePreset = "Today";
        private string categoryFilter = "All";
        private bool calendarPinned;
        private bool tasksPinned;
        private bool calendarActivated;
        private bool tasksActivated;
        private bool syncingRange;
        private bool choosingRangeEnd;
        private bool disposed;
        private bool restoringPins;
        private bool calendarOpen;
        private bool tasksOpen;
        private bool taskActionsOpen;
        private double calendarReveal;
        private double tasksReveal;
        private DateTime lastAnimationTick;
        private DateTime calendarCloseAt = DateTime.MaxValue;
        private DateTime tasksCloseAt = DateTime.MaxValue;
        private OverlayTaskItem removedTask;
        private int removedTaskIndex;

        public event Action<DateTime, DateTime> RangeChanged;
        public event Action ViewReportsRequested;
        public bool CalendarVisible { get { return calendarOpen; } }
        public bool TasksVisible { get { return tasksOpen; } }
        public bool CalendarPinned { get { return calendarPinned; } }
        public bool TasksPinned { get { return tasksPinned; } }
        public DateTime RangeStart { get { return rangeStart; } }
        public DateTime RangeEnd { get { return rangeEnd; } }

        public OverlayPanels(Control host, string dataRoot) : this(host, dataRoot, true) { }

        public OverlayPanels(Control host, string dataRoot, bool persistPins)
        {
            if (host == null) throw new ArgumentNullException("host");
            this.host = host;
            this.persistPins = persistPins;
            panelPreferences = new OverlayPanelPreferenceStore(dataRoot);
            if (persistPins) panelPreferences.Load();
            taskStore = new OverlayTaskStore(dataRoot);
            taskStore.Load();
            rail = new Panel { BackColor = ModernTheme.Background, Width = 44, TabStop = false };
            rail.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen border = new Pen(ModernTheme.Border))
                    e.Graphics.DrawLine(border, rail.Width - 1, 0, rail.Width - 1, rail.Height);
            };
            calendarRail = new RailButton(false) { AccessibleName = "Calendar", AccessibleDescription = "Open calendar. Press Enter or Space to toggle." };
            tasksRail = new RailButton(true) { AccessibleName = "Tasks", AccessibleDescription = "Open tasks. Press Enter or Space to toggle." };
            rail.Controls.Add(calendarRail);
            rail.Controls.Add(tasksRail);
            tips.SetToolTip(calendarRail, "Calendar");
            tips.SetToolTip(tasksRail, "Tasks");
            calendarRail.MouseEnter += delegate { OpenPanel(false); };
            tasksRail.MouseEnter += delegate { OpenPanel(true); };
            calendarRail.MouseLeave += delegate { ScheduleClose(false); };
            tasksRail.MouseLeave += delegate { ScheduleClose(true); };
            calendarRail.Click += delegate { TogglePanel(false); };
            tasksRail.Click += delegate { TogglePanel(true); };
            calendarRail.KeyDown += RailKeyDown;
            tasksRail.KeyDown += RailKeyDown;

            calendarPanel = new ModernCard { Visible = false, TabStop = false, AutoScroll = true, Padding = Padding.Empty, AccessibleName = "Calendar panel" };
            tasksPanel = new ModernCard { Visible = false, TabStop = false, AutoScroll = true, Padding = Padding.Empty, AccessibleName = "Tasks panel" };
            host.Controls.Add(rail);
            host.Controls.Add(calendarPanel);
            host.Controls.Add(tasksPanel);

            AddHeader(calendarPanel, "Calendar", false, out calendarPin);
            AddHeader(tasksPanel, "Tasks", true, out tasksPin);
            previousMonth = QuietButton("<", delegate { NavigateCalendar(-1); });
            nextMonth = QuietButton(">", delegate { NavigateCalendar(1); });
            previousMonth.AccessibleName = "Previous month";
            nextMonth.AccessibleName = "Next month";
            calendarMonth = PanelLabel("", 11, true);
            calendarMonth.TextAlign = ContentAlignment.MiddleCenter;
            calendarPanel.Controls.Add(previousMonth);
            calendarPanel.Controls.Add(nextMonth);
            calendarPanel.Controls.Add(calendarMonth);
            string[] presets = { "Today", "This Week", "This Month", "Custom" };
            for (int i = 0; i < presets.Length; i++)
            {
                string preset = presets[i];
                rangeButtons[i] = QuietButton(preset, delegate { SelectCalendarRange(preset); });
                rangeButtons[i].Font = ModernTheme.Font(8.25f, FontStyle.Regular);
                tips.SetToolTip(rangeButtons[i], preset);
                calendarPanel.Controls.Add(rangeButtons[i]);
            }
            for (int i = 0; i < 7; i++)
            {
                DayOfWeek weekday = (DayOfWeek)(((int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek + i) % 7);
                weekLabels[i] = PanelLabel(CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(weekday), 8);
                weekLabels[i].ForeColor = ModernTheme.Secondary;
                weekLabels[i].TextAlign = ContentAlignment.MiddleCenter;
                calendarPanel.Controls.Add(weekLabels[i]);
            }
            for (int i = 0; i < calendarDays.Length; i++)
            {
                CalendarDayButton day = new CalendarDayButton();
                day.Click += delegate { SelectCalendarDay(day.Date); };
                calendarDays[i] = day;
                calendarPanel.Controls.Add(day);
            }
            fromLabel = PanelLabel("From", 8);
            toLabel = PanelLabel("To", 8);
            fromLabel.ForeColor = toLabel.ForeColor = ModernTheme.Secondary;
            rangeFrom = DatePicker("Range start");
            rangeTo = DatePicker("Range end");
            rangeFrom.ValueChanged += delegate
            {
                if (syncingRange) return;
                rangePreset = "Custom";
                rangeStart = rangeFrom.Value.Date;
                if (rangeEnd < rangeStart) rangeEnd = rangeStart;
                choosingRangeEnd = false;
                RefreshCalendar();
                NotifyRangeChanged();
            };
            rangeTo.ValueChanged += delegate
            {
                if (syncingRange) return;
                rangePreset = "Custom";
                rangeEnd = rangeTo.Value.Date;
                if (rangeStart > rangeEnd) rangeStart = rangeEnd;
                choosingRangeEnd = false;
                RefreshCalendar();
                NotifyRangeChanged();
            };
            calendarSummary = PanelLabel("", 9, true);
            calendarSummary.TextAlign = ContentAlignment.MiddleLeft;
            calendarSummary.BackColor = ModernTheme.Background;
            calendarSummary.Padding = new Padding(12, 5, 8, 5);
            calendarHint = PanelLabel("Select a day, or choose a date range.", 8);
            calendarHint.ForeColor = ModernTheme.Secondary;
            viewReports = QuietButton("View reports", delegate { if (ViewReportsRequested != null) ViewReportsRequested(); });
            viewReports.ForeColor = ModernTheme.Blue;
            viewReports.AccessibleDescription = "Open reports using the selected date range.";
            calendarPanel.Controls.Add(fromLabel);
            calendarPanel.Controls.Add(toLabel);
            calendarPanel.Controls.Add(rangeFrom);
            calendarPanel.Controls.Add(rangeTo);
            calendarPanel.Controls.Add(calendarSummary);
            calendarPanel.Controls.Add(calendarHint);
            calendarPanel.Controls.Add(viewReports);
            calendarPanel.Resize += delegate { LayoutCalendar(); };

            taskInputLabel = PanelLabel("New task", 8, true);
            taskCategoryLabel = PanelLabel("Category", 8, true);
            taskTitle = new TextBox
            {
                BackColor = ModernTheme.Surface, ForeColor = ModernTheme.Text, BorderStyle = BorderStyle.None,
                Font = ModernTheme.Font(9, FontStyle.Regular), MaxLength = 160, AccessibleName = "New task title"
            };
            taskInputCard = new ModernCard { Padding = Padding.Empty, AccessibleName = "New task input" };
            taskInputHint = PanelLabel("Add a task...", 9);
            taskInputHint.ForeColor = ModernTheme.Secondary;
            taskInputHint.Click += delegate { taskTitle.Focus(); };
            Control taskSearchIcon = ModernTheme.Icon("\uE721", ModernTheme.Secondary, 16);
            taskSearchIcon.Tag = "input-icon";
            taskInputCard.Controls.Add(taskSearchIcon);
            taskInputCard.Controls.Add(taskTitle);
            taskInputCard.Controls.Add(taskInputHint);
            taskTitle.TextChanged += delegate { taskInputHint.Visible = taskTitle.Text.Length == 0 && !taskTitle.Focused; };
            taskTitle.GotFocus += delegate { taskInputHint.Visible = false; };
            taskTitle.LostFocus += delegate { taskInputHint.Visible = taskTitle.Text.Length == 0; };
            taskCategory = CategoryPicker();
            taskCategory.AccessibleName = "New task category";
            taskAdd = ModernTheme.Button("+", AddTaskFromInput, true);
            taskAdd.AccessibleName = "Add task";
            taskTitle.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { AddTaskFromInput(); e.Handled = true; e.SuppressKeyPress = true; }
            };
            taskProgressLabel = PanelLabel("", 8);
            taskProgressLabel.ForeColor = ModernTheme.Secondary;
            taskProgressTrack = new Panel { BackColor = ModernTheme.Background };
            taskProgressFill = new Panel { BackColor = ModernTheme.Blue, Dock = DockStyle.Left };
            taskProgressTrack.Controls.Add(taskProgressFill);
            taskList = new Panel { AutoScroll = true, BackColor = ModernTheme.Surface, TabStop = false };
            taskList.Resize += delegate { LayoutTaskRows(); };
            taskStatus = PanelLabel("", 8);
            taskStatus.ForeColor = ModernTheme.Secondary;
            taskStatus.AutoEllipsis = true;
            undoRemove = QuietButton("Undo", UndoRemove);
            undoRemove.Visible = false;
            clearCompleted = QuietButton("Clear completed", ClearCompletedTasks);
            clearCompleted.ForeColor = ModernTheme.Error;
            clearCompleted.AccessibleName = "Clear completed tasks";
            clearCompleted.AccessibleDescription = "Remove every completed task after confirmation.";
            string[] categories = { "All", "Work", "Game", "Personal" };
            for (int i = 0; i < categories.Length; i++)
            {
                string category = categories[i];
                categoryButtons[i] = QuietButton(category, delegate { categoryFilter = category; RenderTasks(); });
                categoryButtons[i].Font = ModernTheme.Font(8, FontStyle.Regular);
                tasksPanel.Controls.Add(categoryButtons[i]);
            }
            tasksPanel.Controls.Add(taskInputLabel);
            tasksPanel.Controls.Add(taskCategoryLabel);
            tasksPanel.Controls.Add(taskInputCard);
            tasksPanel.Controls.Add(taskCategory);
            tasksPanel.Controls.Add(taskAdd);
            tasksPanel.Controls.Add(taskProgressLabel);
            tasksPanel.Controls.Add(taskProgressTrack);
            tasksPanel.Controls.Add(taskList);
            tasksPanel.Controls.Add(taskStatus);
            tasksPanel.Controls.Add(undoRemove);
            tasksPanel.Controls.Add(clearCompleted);
            tasksPanel.Resize += delegate { LayoutTasks(); };

            WireHoverTree(calendarPanel, false);
            WireHoverTree(tasksPanel, true);
            closeTimer.Interval = SystemInformation.IsMenuAnimationEnabled ? 100 : 50;
            closeTimer.Tick += CloseTimerTick;
            closeTimer.Start();
            animationTimer.Interval = 16;
            animationTimer.Tick += AnimationTimerTick;
            host.Resize += HostResize;
            host.FontChanged += HostResize;
            RefreshCalendar();
            RenderTasks();
            UpdateLayout();
            if (persistPins)
            {
                restoringPins = true;
                if (panelPreferences.CalendarPinned) SetPanelPinned("calendar", true);
                if (panelPreferences.TasksPinned) SetPanelPinned("tasks", true);
                restoringPins = false;
                RefreshPinStatus();
            }
        }

        public void UpdateLayout()
        {
            if (disposed) return;
            rail.SetBounds(0, 0, P(44), host.ClientSize.Height);
            calendarRail.SetBounds(P(4), P(12), P(36), P(36));
            tasksRail.SetBounds(P(4), P(56), P(36), P(36));
            int availableWidth = Math.Max(P(240), host.ClientSize.Width - P(72));
            int panelWidth = Math.Min(P(280), availableWidth);
            int availableHeight = Math.Max(P(240), host.ClientSize.Height - P(24));
            int calendarHeight = P(rangePreset == "Custom" ? 440 : 384);
            int tasksHeight = P(358);
            int taskTop = P(12);
            if (calendarOpen && tasksOpen)
            {
                int desired = calendarHeight + tasksHeight + P(ModernTheme.Space);
                if (desired > availableHeight)
                {
                    calendarHeight = Math.Max(P(164), availableHeight / 2 - P(4));
                    tasksHeight = Math.Max(P(120), availableHeight - calendarHeight - P(ModernTheme.Space));
                }
                taskTop += calendarHeight + P(ModernTheme.Space);
            }
            else
            {
                calendarHeight = Math.Min(calendarHeight, availableHeight);
                tasksHeight = Math.Min(P(520), availableHeight);
            }
            calendarPanel.SetBounds(P(52), P(12), panelWidth, calendarHeight);
            tasksPanel.SetBounds(P(52), taskTop, panelWidth, tasksHeight);
            LayoutHeader(calendarPanel);
            LayoutHeader(tasksPanel);
            LayoutCalendar();
            LayoutTasks();
            rail.BringToFront();
            if (calendarPanel.Visible) calendarPanel.BringToFront();
            if (tasksPanel.Visible) tasksPanel.BringToFront();
            calendarRail.IsSelected = calendarOpen;
            tasksRail.IsSelected = tasksOpen;
            calendarRail.Invalidate();
            tasksRail.Invalidate();
            ApplyReveal(calendarPanel, calendarReveal, calendarOpen);
            ApplyReveal(tasksPanel, tasksReveal, tasksOpen);
        }

        private int P(int value) { return ModernTheme.Px(host, value); }
        private Font ScaledFont(float size, FontStyle style = FontStyle.Regular)
        {
            return ModernTheme.Font(size * FontScaleFactor(), style);
        }
        private float FontScaleFactor() { Form form = host.FindForm(); return form == null ? 1 : Math.Max(1, form.Font.SizeInPoints / 9F); }

        // A preview is intentionally held open by pins so image capture is stable.
        public void ShowForPreview(string name)
        {
            string value = (name ?? "").ToLowerInvariant();
            if (value == "calendar" || value == "both" || value == "all") SetPanelPinned("calendar", true);
            if (value == "tasks" || value == "both" || value == "all") SetPanelPinned("tasks", true);
            // Capture helpers request a settled frame, independent of the production reveal timer.
            calendarReveal = calendarOpen ? 1 : 0;
            tasksReveal = tasksOpen ? 1 : 0;
            animationTimer.Stop();
            UpdateLayout();
        }

        // WinForms DrawToBitmap paints overlapping sibling panels in a different order from
        // the live window. Capture the settled overlays last so preview images match their z-order.
        public void DrawPreviewOverlay(Graphics graphics, Control relativeTo)
        {
            if (disposed || graphics == null || relativeTo == null) return;
            Control[] controls = { rail, calendarPanel, tasksPanel };
            foreach (Control control in controls)
            {
                if (!control.Visible || control.Width < 1 || control.Height < 1) continue;
                using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
                {
                    control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    Point position = relativeTo.PointToClient(control.PointToScreen(Point.Empty));
                    graphics.DrawImageUnscaled(bitmap, position);
                }
            }
        }

        public void SetPanelPinned(string name, bool pinned)
        {
            bool tasks = String.Equals(name, "tasks", StringComparison.OrdinalIgnoreCase);
            bool changed = (tasks ? tasksPinned : calendarPinned) != pinned;
            if (tasks) tasksPinned = pinned; else calendarPinned = pinned;
            Button button = tasks ? tasksPin : calendarPin;
            button.Text = pinned ? "\uE840" : "\uE718";
            button.ForeColor = pinned ? ModernTheme.Blue : ModernTheme.Secondary;
            tips.SetToolTip(button, pinned ? "Unpin to close when you leave" : "Keep this panel open");
            button.AccessibleName = (pinned ? "Unpin " : "Pin ") + (tasks ? "tasks" : "calendar");
            if (pinned) OpenPanel(tasks); else ScheduleClose(tasks);
            if (changed && persistPins && !restoringPins)
            {
                panelPreferences.CalendarPinned = calendarPinned;
                panelPreferences.TasksPinned = tasksPinned;
                panelPreferences.Save();
            }
            RefreshPinStatus();
        }

        public void SelectCalendarRange(string preset)
        {
            DateTime today = DateTime.Today;
            rangePreset = preset;
            choosingRangeEnd = false;
            if (preset == "Today") rangeStart = rangeEnd = today;
            else if (preset == "This Week")
            {
                int firstDay = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                rangeStart = today.AddDays(-(((int)today.DayOfWeek - firstDay + 7) % 7));
                rangeEnd = today;
            }
            else if (preset == "This Month")
            {
                rangeStart = new DateTime(today.Year, today.Month, 1);
                rangeEnd = today;
            }
            else rangePreset = "Custom";
            month = new DateTime(rangeStart.Year, rangeStart.Month, 1);
            RefreshCalendar();
            if (rangePreset != "Custom") NotifyRangeChanged();
        }

        public void NavigateCalendar(int months)
        {
            DateTime candidate;
            try { candidate = month.AddMonths(months); }
            catch (ArgumentOutOfRangeException) { return; }
            if (candidate.Year < 1900 || candidate > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)) return;
            month = candidate;
            RefreshCalendar();
        }

        private void AddHeader(ModernCard panel, string title, bool tasks, out Button pin)
        {
            Label icon = PanelLabel(tasks ? "\uE9D5" : "\uE787", 11);
            icon.Font = new Font("Segoe MDL2 Assets", 11);
            icon.ForeColor = ModernTheme.Blue;
            icon.TextAlign = ContentAlignment.MiddleCenter;
            icon.SetBounds(16, 9, 20, 30);
            icon.Tag = "header-icon";
            panel.Controls.Add(icon);
            Label label = PanelLabel(title, 10, true);
            label.SetBounds(44, 9, 124, 30);
            label.Tag = "header-title";
            panel.Controls.Add(label);
            Button pinButton = QuietButton("\uE718", delegate { SetPanelPinned(tasks ? "tasks" : "calendar", !(tasks ? tasksPinned : calendarPinned)); });
            pinButton.Font = new Font("Segoe MDL2 Assets", 10);
            pinButton.Width = 32;
            pinButton.Height = 32;
            pinButton.Top = 8;
            pinButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            pinButton.Left = panel.Width - 84;
            pinButton.AccessibleName = "Pin " + title.ToLowerInvariant();
            pinButton.Tag = "header-pin";
            panel.Controls.Add(pinButton);
            Button close = QuietButton("\uE711", delegate
            {
                SetPanelPinned(tasks ? "tasks" : "calendar", false);
                HidePanel(tasks);
            });
            close.Font = new Font("Segoe MDL2 Assets", 9);
            close.SetBounds(panel.Width - 48, 8, 32, 32);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.AccessibleName = "Close " + title.ToLowerInvariant();
            close.Tag = "header-close";
            tips.SetToolTip(close, "Close " + title.ToLowerInvariant());
            panel.Controls.Add(close);
            pin = pinButton;
        }

        private void LayoutHeader(ModernCard panel)
        {
            int width = PanelContentWidth(panel);
            foreach (Control child in panel.Controls)
            {
                string part = child.Tag as string;
                if (part == "header-icon") child.SetBounds(P(16), P(9), P(20), P(30));
                else if (part == "header-title") child.SetBounds(P(44), P(9), Math.Max(P(56), width - P(132)), P(30));
                else if (part == "header-pin") child.SetBounds(width - P(84), P(8), P(32), P(32));
                else if (part == "header-close") child.SetBounds(width - P(48), P(8), P(32), P(32));
            }
        }

        private int PanelContentWidth(ModernCard panel)
        {
            int contentHeight = panel == calendarPanel ? P(rangePreset == "Custom" ? 440 : 384) : Math.Max(P(186), panel.Height - P(68)) + P(62);
            return panel.Width - (contentHeight > panel.Height ? SystemInformation.VerticalScrollBarWidth : 0);
        }

        private static Label PanelLabel(string text, float size = 9, bool bold = false)
        {
            Label label = ModernTheme.Label(text, size, bold);
            label.AutoSize = false;
            return label;
        }

        private Button QuietButton(string text, Action action)
        {
            Button button = ModernTheme.Button(text, action, false);
            button.BackColor = ModernTheme.Surface;
            button.ForeColor = ModernTheme.Secondary;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ModernTheme.Background;
            button.Margin = Padding.Empty;
            return button;
        }

        private static DateTimePicker DatePicker(string name)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "MMM d, yyyy", Font = ModernTheme.Font(8.5f, FontStyle.Regular),
                CalendarMonthBackground = ModernTheme.Surface, CalendarForeColor = ModernTheme.Text, AccessibleName = name,
                MinDate = new DateTime(1900, 1, 1), MaxDate = DateTime.Today
            };
        }

        private static ComboBox CategoryPicker()
        {
            ComboBox combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, BackColor = ModernTheme.Background, ForeColor = ModernTheme.Text,
                FlatStyle = FlatStyle.Flat, Font = ModernTheme.Font(9, FontStyle.Regular)
            };
            combo.Items.AddRange(new object[] { "Work", "Game", "Personal" });
            combo.SelectedIndex = 0;
            return combo;
        }

        private void LayoutCalendar()
        {
            int width = PanelContentWidth(calendarPanel);
            // A full six-week grid keeps month navigation stable without making the overlay taller.
            previousMonth.SetBounds(width - P(80), P(48), P(32), P(32));
            calendarMonth.TextAlign = ContentAlignment.MiddleLeft;
            calendarMonth.SetBounds(P(16), P(48), width - P(102), P(32));
            nextMonth.SetBounds(width - P(48), P(48), P(32), P(32));
            LayoutCompactButtons(rangeButtons, P(16), P(84), width - P(32), P(28));
            int dayWidth = (width - P(32)) / 7;
            int dayHeight = P(26);
            for (int i = 0; i < 7; i++) weekLabels[i].SetBounds(P(16) + i * dayWidth, P(116), dayWidth, P(20));
            for (int i = 0; i < 42; i++) calendarDays[i].SetBounds(P(16) + (i % 7) * dayWidth, P(140) + (i / 7) * dayHeight, dayWidth - P(2), dayHeight - P(2));
            calendarSummary.SetBounds(P(16), P(300), width - P(32), P(48));
            calendarSummary.Padding = new Padding(P(12), P(5), P(8), P(5));
            bool custom = rangePreset == "Custom";
            fromLabel.Visible = toLabel.Visible = rangeFrom.Visible = rangeTo.Visible = custom;
            int pickerWidth = (width - P(40)) / 2;
            fromLabel.SetBounds(P(16), P(352), pickerWidth, P(16));
            toLabel.SetBounds(P(24) + pickerWidth, P(352), pickerWidth, P(16));
            rangeFrom.SetBounds(P(16), P(376), pickerWidth, P(26));
            rangeTo.SetBounds(P(24) + pickerWidth, P(376), pickerWidth, P(26));
            int actionTop = P(custom ? 408 : 352);
            viewReports.SetBounds(width - P(128), actionTop, P(112), P(28));
            calendarHint.SetBounds(P(16), actionTop, width - P(152), P(32));
            calendarHint.Visible = custom;
            calendarPanel.AutoScrollMinSize = new Size(0, actionTop + P(32));
        }

        private void RefreshCalendar()
        {
            calendarMonth.Text = month.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            nextMonth.Enabled = month < new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            int firstDay = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            DateTime gridStart = month.AddDays(-(((int)month.DayOfWeek - firstDay + 7) % 7));
            for (int i = 0; i < 42; i++)
            {
                CalendarDayButton day = calendarDays[i];
                day.Date = gridStart.AddDays(i);
                day.IsCurrentMonth = day.Date.Month == month.Month;
                day.Enabled = day.Date >= rangeFrom.MinDate.Date && day.Date <= rangeFrom.MaxDate.Date;
                day.IsSelectedDate = day.Date >= rangeStart && day.Date <= rangeEnd;
                day.IsRangeBoundary = day.Date == rangeStart || day.Date == rangeEnd;
                day.AccessibleName = day.Date.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture) + (day.IsSelectedDate ? ", selected" : "");
                day.Invalidate();
            }
            for (int i = 0; i < rangeButtons.Length; i++)
            {
                bool selected = rangeButtons[i].Text == rangePreset;
                rangeButtons[i].BackColor = selected ? ModernTheme.Background : ModernTheme.Surface;
                rangeButtons[i].ForeColor = selected ? ModernTheme.Blue : ModernTheme.Secondary;
            }
            syncingRange = true;
            rangeFrom.Value = ClampPickerDate(rangeStart);
            rangeTo.Value = ClampPickerDate(rangeEnd);
            syncingRange = false;
            int days = (int)(rangeEnd - rangeStart).TotalDays + 1;
            string dates = rangeStart == rangeEnd ? rangeStart.ToString("ddd, MMM d, yyyy") : rangeStart.ToString("MMM d") + " - " + rangeEnd.ToString("MMM d, yyyy");
            calendarSummary.Text = dates + Environment.NewLine + days + (days == 1 ? " day selected" : " days selected");
            calendarHint.Text = choosingRangeEnd ? "Choose the last day of your range." : (rangePreset == "Custom" ? "Choose two days to select a range, or edit the dates." : "Select a day, or choose a date range.");
            UpdateLayout();
        }

        private void NotifyRangeChanged()
        {
            Action<DateTime, DateTime> handler = RangeChanged;
            if (handler != null) handler(rangeStart, rangeEnd);
        }

        private static DateTime ClampPickerDate(DateTime date)
        {
            return date < new DateTime(1900, 1, 1) ? new DateTime(1900, 1, 1) : (date > DateTime.Today ? DateTime.Today : date);
        }

        private void SelectCalendarDay(DateTime date)
        {
            if (date.Date < rangeFrom.MinDate.Date || date.Date > DateTime.Today) return;
            date = date.Date;
            if (rangePreset == "Custom" && choosingRangeEnd)
            {
                if (date < rangeStart) { rangeEnd = rangeStart; rangeStart = date; }
                else rangeEnd = date;
                choosingRangeEnd = false;
            }
            else
            {
                rangePreset = "Custom";
                rangeStart = rangeEnd = date;
                choosingRangeEnd = true;
            }
            RefreshCalendar();
            NotifyRangeChanged();
        }

        private void LayoutTasks()
        {
            int width = PanelContentWidth(tasksPanel);
            taskInputLabel.Visible = false;
            taskInputCard.SetBounds(P(16), P(84), width - P(76), P(36));
            taskTitle.SetBounds(P(32), P(9), taskInputCard.Width - P(40), P(23));
            taskInputHint.SetBounds(P(32), P(7), taskInputCard.Width - P(40), P(24));
            foreach (Control child in taskInputCard.Controls) if ((child.Tag as string) == "input-icon") child.SetBounds(P(10), P(10), P(16), P(16));
            taskAdd.SetBounds(width - P(52), P(84), P(36), P(36));
            taskCategoryLabel.SetBounds(P(16), P(122), P(66), P(24));
            taskCategory.SetBounds(P(84), P(122), width - P(100), P(26));
            LayoutCompactButtons(categoryButtons, P(16), P(48), width - P(32), P(28));
            int footerTop = Math.Max(P(186), tasksPanel.Height - P(68));
            taskList.SetBounds(P(12), P(158), width - P(24), Math.Max(P(24), footerTop - P(166)));
            taskProgressLabel.SetBounds(P(16), footerTop, width - P(clearCompleted.Visible ? 138 : 32), P(19));
            clearCompleted.SetBounds(width - P(122), footerTop - P(6), P(106), P(28));
            taskProgressTrack.SetBounds(P(16), footerTop + P(22), width - P(32), P(8));
            using (GraphicsPath trackShape = ModernTheme.Rounded(new Rectangle(0, 0, taskProgressTrack.Width, taskProgressTrack.Height), P(4)))
            {
                Region previousRegion = taskProgressTrack.Region;
                taskProgressTrack.Region = new Region(trackShape);
                if (previousRegion != null) previousRegion.Dispose();
            }
            taskStatus.SetBounds(P(16), footerTop + P(34), width - P(undoRemove.Visible ? 84 : 32), P(23));
            undoRemove.SetBounds(width - P(68), footerTop + P(31), P(52), P(28));
            tasksPanel.AutoScrollMinSize = new Size(0, footerTop + P(62));
            SetProgressWidth();
            LayoutTaskRows();
        }

        private void LayoutCompactButtons(Button[] buttons, int left, int top, int width, int height)
        {
            float logicalFontSize = Object.ReferenceEquals(buttons, rangeButtons) ? 8.25F : 8F;
            float desiredFontSize = logicalFontSize * FontScaleFactor();
            foreach (Button button in buttons)
            {
                if (Math.Abs(button.Font.SizeInPoints - desiredFontSize) > .01F)
                {
                    Font prior = button.Font;
                    button.Font = ModernTheme.Font(desiredFontSize);
                    prior.Dispose();
                }
            }
            int[] preferred = buttons.Select(button => TextRenderer.MeasureText(button.Text, button.Font, new Size(1000, 200), TextFormatFlags.NoPadding).Width + P(8)).ToArray();
            int usable = width - (buttons.Length - 1) * P(4);
            int sum = preferred.Sum();
            if (sum > usable)
            {
                float adjusted = desiredFontSize * (usable - buttons.Length * P(8)) / Math.Max(1F, sum - buttons.Length * P(8));
                foreach (Button button in buttons)
                {
                    Font prior = button.Font;
                    button.Font = ModernTheme.Font(Math.Max(7F * FontScaleFactor(), adjusted - .1F));
                    prior.Dispose();
                }
                preferred = buttons.Select(button => TextRenderer.MeasureText(button.Text, button.Font, new Size(1000, 200), TextFormatFlags.NoPadding).Width + P(8)).ToArray();
                sum = preferred.Sum();
            }
            int at = left;
            for (int i = 0; i < buttons.Length; i++)
            {
                int buttonWidth = i == buttons.Length - 1 ? left + width - at : Math.Max(P(32), sum <= usable ? preferred[i] + (usable - sum) / buttons.Length : (int)Math.Round((double)usable * preferred[i] / sum));
                buttons[i].SetBounds(at, top, buttonWidth, height);
                at += buttonWidth + P(4);
            }
        }

        private void RenderTasks()
        {
            string focusedRowId = null;
            string focusedPart = null;
            foreach (Control row in taskList.Controls)
            {
                if (!row.ContainsFocus) continue;
                focusedRowId = row.Tag as string;
                foreach (Control child in row.Controls) if (child.ContainsFocus) focusedPart = child.Tag as string;
                break;
            }
            taskList.SuspendLayout();
            while (taskList.Controls.Count > 0) taskList.Controls[0].Dispose();
            string[] categories = { "All", "Work", "Game", "Personal" };
            for (int i = 0; i < categories.Length; i++)
            {
                string category = categories[i];
                int count = taskStore.Items.Count(t => !t.Completed && (category == "All" || t.Category == category));
                categoryButtons[i].Text = category + " (" + count + ")";
                categoryButtons[i].ForeColor = categoryFilter == category ? ModernTheme.Blue : ModernTheme.Secondary;
                categoryButtons[i].BackColor = categoryFilter == category ? ModernTheme.Background : ModernTheme.Surface;
                categoryButtons[i].AccessibleName = category + ", " + count + " open tasks";
            }
            List<OverlayTaskItem> visible = taskStore.Items.Where(t => categoryFilter == "All" || t.Category == categoryFilter).OrderBy(t => t.Completed).ThenByDescending(t => t.CreatedUtc).ToList();
            if (visible.Count == 0)
            {
                Panel empty = new Panel { Height = 106, BackColor = ModernTheme.Surface, Tag = "empty" };
                Label heading = PanelLabel(categoryFilter == "All" ? "Your tasks, in one place" : "No " + categoryFilter.ToLowerInvariant() + " tasks yet", 10, true);
                heading.Font = ScaledFont(10, FontStyle.Bold);
                heading.TextAlign = ContentAlignment.MiddleCenter;
                heading.Tag = "heading";
                Label detail = PanelLabel("Add a task above to keep track of what is next.", 9);
                detail.Font = ScaledFont(9);
                detail.TextAlign = ContentAlignment.TopCenter;
                detail.ForeColor = ModernTheme.Secondary;
                detail.Tag = "detail";
                empty.Controls.Add(heading);
                empty.Controls.Add(detail);
                taskList.Controls.Add(empty);
            }
            else
            {
                foreach (OverlayTaskItem item in visible) taskList.Controls.Add(CreateTaskRow(item));
            }
            int complete = taskStore.Items.Count(t => t.Completed);
            taskProgressLabel.Text = complete + " of " + taskStore.Items.Count + " completed" + (taskStore.Items.Count == 0 ? "" : "  /  " + Math.Round(100.0 * complete / taskStore.Items.Count) + "%");
            clearCompleted.Visible = complete > 0;
            if (!String.IsNullOrEmpty(taskStore.LastError)) { taskStatus.Text = taskStore.LastError; taskStatus.ForeColor = ModernTheme.Warning; }
            else if (removedTask == null) { taskStatus.Text = "Saved on this computer"; taskStatus.ForeColor = ModernTheme.Secondary; }
            taskList.ResumeLayout();
            LayoutTasks();
            if (focusedRowId != null)
            {
                Control replacement = taskList.Controls.Cast<Control>().FirstOrDefault(row => String.Equals(row.Tag as string, focusedRowId, StringComparison.Ordinal));
                Control focus = replacement == null ? null : replacement.Controls.Cast<Control>().FirstOrDefault(child => String.Equals(child.Tag as string, focusedPart, StringComparison.Ordinal));
                if (focus != null && focus.CanFocus) focus.Focus(); else taskTitle.Focus();
            }
        }

        private Panel CreateTaskRow(OverlayTaskItem item)
        {
            Panel row = new Panel { Height = P(36), BackColor = ModernTheme.Surface, Font = ScaledFont(9), Tag = item.Id };
            CheckBox check = new CheckBox
            {
                Checked = item.Completed, FlatStyle = FlatStyle.Standard, ForeColor = ModernTheme.Blue,
                BackColor = ModernTheme.Surface, AccessibleName = "Complete " + item.Title, Tag = "check"
            };
            check.CheckedChanged += delegate { MutateTasks(delegate { item.Completed = check.Checked; }, "Task updated"); };
            Label title = PanelLabel(item.Title, 9, false);
            title.Font = ScaledFont(9);
            title.AutoEllipsis = true;
            title.ForeColor = item.Completed ? ModernTheme.Secondary : ModernTheme.Text;
            if (item.Completed) title.Font = ScaledFont(9, FontStyle.Strikeout);
            title.Tag = "title";
            tips.SetToolTip(title, item.Title);
            title.DoubleClick += delegate { EditTask(item); };
            Label category = PanelLabel(item.Category, 7.75f);
            category.Font = ScaledFont(7.75f);
            category.ForeColor = item.Category == "Game" ? ModernTheme.Game : item.Category == "Work" ? ModernTheme.Blue : ModernTheme.Secondary;
            category.BackColor = ModernTheme.Tint(category.ForeColor, .10);
            category.TextAlign = ContentAlignment.MiddleCenter;
            category.Tag = "category";
            Panel dot = new Panel { BackColor = category.ForeColor, Tag = "category-dot" };
            ContextMenuStrip actions = new ContextMenuStrip { Font = ScaledFont(9), BackColor = ModernTheme.Surface, ForeColor = ModernTheme.Text };
            actions.Items.Add("Edit", null, delegate { EditTask(item); });
            ToolStripItem remove = actions.Items.Add("Remove", null, delegate
            {
                int index = taskStore.Items.IndexOf(item);
                if (MutateTasks(delegate { taskStore.Items.Remove(item); }, "Task removed"))
                {
                    removedTask = item; removedTaskIndex = index;
                    taskStatus.Text = "Task removed"; undoRemove.Visible = true; LayoutTasks();
                }
            });
            remove.ForeColor = ModernTheme.Error;
            row.ContextMenuStrip = actions;
            row.Disposed += delegate { actions.Dispose(); };
            Button menu = QuietButton("Task actions", null);
            menu.Text = "\uE712";
            menu.Font = new Font("Segoe MDL2 Assets", 9 * FontScaleFactor());
            menu.BackColor = ModernTheme.Surface;
            menu.Tag = "actions";
            menu.AccessibleName = "Actions for " + item.Title;
            menu.Click += delegate { actions.Show(menu, new Point(menu.Width, menu.Height), ToolStripDropDownDirection.BelowLeft); };
            actions.Opened += delegate { taskActionsOpen = true; tasksCloseAt = DateTime.MaxValue; };
            actions.Closed += delegate { taskActionsOpen = false; ScheduleClose(true); };
            tips.SetToolTip(menu, "Edit or remove " + item.Title);
            row.Controls.Add(check);
            row.Controls.Add(title);
            row.Controls.Add(category);
            row.Controls.Add(dot);
            row.Controls.Add(menu);
            WireHoverTree(row, true);
            return row;
        }

        private void LayoutTaskRows()
        {
            int totalHeight = taskList.Controls.Cast<Control>().Sum(row => P((row.Tag as string) == "empty" ? 106 : 36) + P(4));
            int width = Math.Max(P(100), taskList.Width - (totalHeight > taskList.Height ? SystemInformation.VerticalScrollBarWidth : 0) - P(8));
            int y = 0;
            foreach (Control row in taskList.Controls)
            {
                bool empty = (row.Tag as string) == "empty";
                row.SetBounds(P(4), y, width, P(empty ? 106 : 36));
                if (empty)
                {
                    foreach (Control child in row.Controls)
                    {
                        if ((child.Tag as string) == "heading") child.SetBounds(P(8), P(16), width - P(16), P(30));
                        else child.SetBounds(P(12), P(52), width - P(24), P(48));
                    }
                }
                else
                {
                    foreach (Control child in row.Controls)
                    {
                        string tag = child.Tag as string;
                        if (tag == "check") child.SetBounds(0, P(8), P(18), P(20));
                        else if (tag == "category-dot") child.SetBounds(P(22), P(14), P(8), P(8));
                        else if (tag == "title") child.SetBounds(P(36), P(4), Math.Max(P(40), width - P(116)), P(28));
                        else if (tag == "category") child.SetBounds(width - P(76), P(7), P(46), P(22));
                        else if (tag == "actions") child.SetBounds(width - P(26), P(4), P(24), P(28));
                        if (tag == "category" || tag == "category-dot")
                        {
                            using (GraphicsPath shape = ModernTheme.Rounded(new Rectangle(0, 0, child.Width, child.Height), P(tag == "category-dot" ? 4 : 8)))
                            {
                                Region prior = child.Region;
                                child.Region = new Region(shape);
                                if (prior != null) prior.Dispose();
                            }
                        }
                    }
                }
                y += row.Height + P(4);
            }
            taskList.AutoScrollMinSize = new Size(0, y);
            taskList.PerformLayout();
            // Every row is sized to the vertical-scroll viewport; a stale horizontal
            // range from resizing must not reduce an already short task viewport.
            taskList.HorizontalScroll.Visible = false;
        }

        private void SetProgressWidth()
        {
            int done = taskStore.Items.Count(t => t.Completed);
            taskProgressFill.Width = taskStore.Items.Count == 0 ? 0 : (int)Math.Round((double)taskProgressTrack.Width * done / taskStore.Items.Count);
        }

        private void AddTaskFromInput()
        {
            string title = taskTitle.Text.Trim();
            if (title.Length == 0) { taskStatus.Text = "Enter a title for your task."; taskStatus.ForeColor = ModernTheme.Warning; taskTitle.Focus(); return; }
            if (taskStore.Items.Count >= OverlayTaskStore.MaximumTasks) { taskStatus.Text = "The task list is full. Remove a task to add another."; taskStatus.ForeColor = ModernTheme.Warning; return; }
            OverlayTaskItem item = new OverlayTaskItem { Id = Guid.NewGuid().ToString("N"), Title = title, Category = taskCategory.Text, CreatedUtc = DateTime.UtcNow };
            if (MutateTasks(delegate { taskStore.Items.Add(item); }, "Task added")) { taskTitle.Clear(); taskTitle.Focus(); }
        }

        private void EditTask(OverlayTaskItem item)
        {
            // Pin temporarily while a child dialog owns keyboard focus.
            bool wasPinned = tasksPinned;
            tasksPinned = true;
            using (Form dialog = new Form())
            {
                dialog.Text = "Edit task";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.AutoScaleMode = AutoScaleMode.None;
                dialog.ClientSize = new Size(P(390), P(213));
                dialog.Font = ScaledFont(9);
                dialog.BackColor = ModernTheme.Surface;
                dialog.ForeColor = ModernTheme.Text;
                Label titleLabel = PanelLabel("Task", 9, true);
                titleLabel.Font = ScaledFont(9, FontStyle.Bold);
                titleLabel.SetBounds(P(22), P(20), P(340), P(22));
                TextBox title = new TextBox { Text = item.Title, BackColor = ModernTheme.Background, ForeColor = ModernTheme.Text, BorderStyle = BorderStyle.FixedSingle, Font = ScaledFont(10), MaxLength = 160 };
                title.SetBounds(P(22), P(48), P(346), P(27));
                Label categoryLabel = PanelLabel("Category", 9, true);
                categoryLabel.Font = ScaledFont(9, FontStyle.Bold);
                categoryLabel.SetBounds(P(22), P(93), P(120), P(22));
                ComboBox category = CategoryPicker();
                category.Font = ScaledFont(9);
                category.SelectedItem = item.Category;
                category.SetBounds(P(22), P(120), P(164), P(27));
                Button save = ModernTheme.Button("Save changes", delegate
                {
                    if (title.Text.Trim().Length == 0) { title.Focus(); return; }
                    if (MutateTasks(delegate { item.Title = title.Text.Trim(); item.Category = category.Text; }, "Task saved")) dialog.DialogResult = DialogResult.OK;
                }, true);
                save.Font = ScaledFont(9, FontStyle.Bold);
                save.SetBounds(P(236), P(164), P(132), P(32));
                Button cancel = QuietButton("Cancel", delegate { dialog.DialogResult = DialogResult.Cancel; });
                cancel.Font = ScaledFont(9, FontStyle.Bold);
                cancel.SetBounds(P(146), P(164), P(82), P(32));
                dialog.Controls.AddRange(new Control[] { titleLabel, title, categoryLabel, category, save, cancel });
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;
                dialog.Shown += delegate { title.SelectAll(); title.Focus(); };
                dialog.ShowDialog(host.FindForm());
            }
            tasksPinned = wasPinned;
            ScheduleClose(true);
        }

        private bool MutateTasks(Action change, string status)
        {
            List<OverlayTaskItem> originals = taskStore.Items.ToList();
            List<OverlayTaskItem> before = originals.Select(t => t.Copy()).ToList();
            change();
            if (!taskStore.Save())
            {
                for (int i = 0; i < originals.Count; i++)
                {
                    originals[i].Id = before[i].Id; originals[i].Title = before[i].Title;
                    originals[i].Category = before[i].Category; originals[i].Completed = before[i].Completed;
                    originals[i].CreatedUtc = before[i].CreatedUtc;
                }
                taskStore.Items.Clear(); taskStore.Items.AddRange(originals);
                RenderTasks();
                return false;
            }
            taskStatus.Text = status;
            taskStatus.ForeColor = ModernTheme.Secondary;
            RenderTasks();
            return true;
        }

        private void UndoRemove()
        {
            if (removedTask == null) return;
            OverlayTaskItem item = removedTask;
            if (MutateTasks(delegate { taskStore.Items.Insert(Math.Min(removedTaskIndex, taskStore.Items.Count), item); }, "Task restored"))
            {
                removedTask = null;
                undoRemove.Visible = false;
                taskStatus.Text = "Task restored";
                LayoutTasks();
            }
        }

        private void ClearCompletedTasks()
        {
            int complete = taskStore.Items.Count(t => t.Completed);
            if (complete == 0) return;
            string noun = complete == 1 ? "task" : "tasks";
            if (ShowClearCompletedConfirmation(complete, noun) != DialogResult.Yes) return;
            OverlayTaskItem previousRemoved = removedTask;
            int previousIndex = removedTaskIndex;
            bool previousUndoVisible = undoRemove.Visible;
            removedTask = null;
            undoRemove.Visible = false;
            if (!MutateTasks(delegate { taskStore.Items.RemoveAll(t => t.Completed); }, "Completed tasks cleared"))
            {
                removedTask = previousRemoved;
                removedTaskIndex = previousIndex;
                undoRemove.Visible = previousUndoVisible;
                LayoutTasks();
            }
        }

        private DialogResult ShowClearCompletedConfirmation(int complete, string noun)
        {
            string description = "Remove " + complete + " completed " + noun + "? This keeps every open task.";
            Type alertType = typeof(OverlayPanels).Assembly.GetType("GameManagement.ModernAlertForm", false);
            if (alertType != null)
            {
                System.Reflection.MethodInfo show = alertType.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .FirstOrDefault(method => method.Name == "Show" && method.GetParameters().Length == 8);
                if (show != null)
                    return (DialogResult)show.Invoke(null, new object[] { host.FindForm(), "Clear completed tasks", description,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, "Clear", "Keep", null });
            }
            return MessageBox.Show(host.FindForm(), description, "Clear completed tasks", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        }

        private void OpenPanel(bool tasks)
        {
            if (disposed) return;
            if (tasks)
            {
                tasksCloseAt = DateTime.MaxValue;
                if (!calendarPinned && !calendarPanel.ContainsFocus) HidePanel(false);
                tasksOpen = true;
            }
            else
            {
                calendarCloseAt = DateTime.MaxValue;
                if (!tasksPinned && !tasksPanel.ContainsFocus) HidePanel(true);
                calendarOpen = true;
            }
            StartReveal(tasks);
            UpdateLayout();
        }

        private void HidePanel(bool tasks)
        {
            if (tasks) { tasksOpen = false; tasksActivated = false; tasksCloseAt = DateTime.MaxValue; }
            else { calendarOpen = false; calendarActivated = false; calendarCloseAt = DateTime.MaxValue; }
            StartReveal(tasks);
            UpdateLayout();
        }

        private void StartReveal(bool tasks)
        {
            bool open = tasks ? tasksOpen : calendarOpen;
            ModernCard panel = tasks ? tasksPanel : calendarPanel;
            if (!SystemInformation.IsMenuAnimationEnabled || !host.IsHandleCreated)
            {
                if (tasks) tasksReveal = open ? 1 : 0; else calendarReveal = open ? 1 : 0;
                ApplyReveal(panel, open ? 1 : 0, open);
                return;
            }
            if (open)
            {
                panel.Visible = true;
                // Keep native edit controls in place; only reveal the overlay's clipped region.
                if (tasks && tasksReveal == 0) tasksReveal = .025;
                if (!tasks && calendarReveal == 0) calendarReveal = .025;
            }
            if (!animationTimer.Enabled) lastAnimationTick = DateTime.UtcNow;
            animationTimer.Start();
        }

        private void AnimationTimerTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (!SystemInformation.IsMenuAnimationEnabled)
            {
                calendarReveal = calendarOpen ? 1 : 0;
                tasksReveal = tasksOpen ? 1 : 0;
            }
            double step = Math.Min(.5, Math.Max(0, (now - lastAnimationTick).TotalMilliseconds / 200.0));
            lastAnimationTick = now;
            calendarReveal = calendarOpen ? Math.Min(1, calendarReveal + step) : Math.Max(0, calendarReveal - step);
            tasksReveal = tasksOpen ? Math.Min(1, tasksReveal + step) : Math.Max(0, tasksReveal - step);
            ApplyReveal(calendarPanel, calendarReveal, calendarOpen);
            ApplyReveal(tasksPanel, tasksReveal, tasksOpen);
            if ((calendarReveal == 0 || calendarReveal == 1) && (tasksReveal == 0 || tasksReveal == 1)) animationTimer.Stop();
        }

        private static void ApplyReveal(Control panel, double amount, bool open)
        {
            if (amount <= 0 && !open)
            {
                panel.Visible = false;
                Region old = panel.Region;
                panel.Region = null;
                if (old != null) old.Dispose();
                return;
            }
            panel.Visible = true;
            Region previous = panel.Region;
            panel.Region = amount >= 1 ? null : new Region(new Rectangle(0, 0, Math.Max(1, (int)Math.Round(panel.Width * amount)), panel.Height));
            if (previous != null) previous.Dispose();
        }

        private void RefreshPinStatus()
        {
            if (String.IsNullOrEmpty(panelPreferences.LastError))
            {
                calendarPin.ForeColor = calendarPinned ? ModernTheme.Blue : ModernTheme.Secondary;
                tasksPin.ForeColor = tasksPinned ? ModernTheme.Blue : ModernTheme.Secondary;
                calendarPin.AccessibleDescription = tasksPin.AccessibleDescription = "";
                tips.SetToolTip(calendarPin, calendarPinned ? "Unpin to close when you leave" : "Keep this panel open");
                tips.SetToolTip(tasksPin, tasksPinned ? "Unpin to close when you leave" : "Keep this panel open");
                return;
            }
            calendarPin.ForeColor = tasksPin.ForeColor = ModernTheme.Warning;
            tips.SetToolTip(calendarPin, panelPreferences.LastError);
            tips.SetToolTip(tasksPin, panelPreferences.LastError);
            calendarPin.AccessibleDescription = tasksPin.AccessibleDescription = panelPreferences.LastError;
        }

        private void TogglePanel(bool tasks)
        {
            bool visible = tasks ? TasksVisible : CalendarVisible;
            bool activated = tasks ? tasksActivated : calendarActivated;
            if (visible && (activated || (tasks ? tasksPinned : calendarPinned)))
            {
                SetPanelPinned(tasks ? "tasks" : "calendar", false);
                HidePanel(tasks);
            }
            else
            {
                OpenPanel(tasks);
                if (tasks) tasksActivated = true; else calendarActivated = true;
                if (tasks) taskTitle.Focus(); else previousMonth.Focus();
            }
        }

        private void ScheduleClose(bool tasks)
        {
            // A short grace period lets the pointer cross the gap between rail and card.
            DateTime at = DateTime.UtcNow.AddMilliseconds(300);
            if (tasks) tasksCloseAt = at; else calendarCloseAt = at;
        }

        private void WireHoverTree(Control control, bool tasks)
        {
            control.MouseEnter += delegate { if (tasks) tasksCloseAt = DateTime.MaxValue; else calendarCloseAt = DateTime.MaxValue; };
            control.MouseLeave += delegate { ScheduleClose(tasks); };
            control.Leave += delegate { ScheduleClose(tasks); };
            control.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (!(tasks ? tasksPinned : calendarPinned)) HidePanel(tasks);
                    (tasks ? tasksRail : calendarRail).Focus();
                    e.Handled = true; e.SuppressKeyPress = true;
                }
            };
            foreach (Control child in control.Controls) WireHoverTree(child, tasks);
        }

        private void CloseTimerTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (CalendarVisible && !calendarPinned && now >= calendarCloseAt && !PanelIsActive(calendarPanel, calendarRail)) HidePanel(false);
            if (TasksVisible && !tasksPinned && !taskActionsOpen && now >= tasksCloseAt && !PanelIsActive(tasksPanel, tasksRail)) HidePanel(true);
        }

        private static bool PanelIsActive(Control panel, Control button)
        {
            Point pointer = Cursor.Position;
            return panel.ContainsFocus || button.ContainsFocus || panel.RectangleToScreen(panel.ClientRectangle).Contains(pointer) || button.RectangleToScreen(button.ClientRectangle).Contains(pointer);
        }

        private void RailKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (!calendarPinned) HidePanel(false);
                if (!tasksPinned) HidePanel(true);
                e.Handled = true; e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                (sender == calendarRail ? tasksRail : calendarRail).Focus();
                e.Handled = true; e.SuppressKeyPress = true;
            }
        }

        private void HostResize(object sender, EventArgs e) { UpdateLayout(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            host.Resize -= HostResize;
            host.FontChanged -= HostResize;
            closeTimer.Stop(); closeTimer.Dispose(); tips.Dispose();
            animationTimer.Stop(); animationTimer.Dispose();
            calendarPanel.Dispose(); tasksPanel.Dispose(); rail.Dispose();
        }

        private sealed class CalendarDayButton : Button
        {
            public DateTime Date { get; set; }
            public bool IsCurrentMonth;
            public bool IsSelectedDate;
            public bool IsRangeBoundary;
            private bool hovered;
            public CalendarDayButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = ModernTheme.Surface;
                Font = ModernTheme.Font(9, FontStyle.Regular);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                MouseEnter += delegate { hovered = true; Invalidate(); };
                MouseLeave += delegate { hovered = false; Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(ModernTheme.Surface);
                int inset = ModernTheme.Px(this, 1);
                Rectangle rect = new Rectangle(inset, inset, Math.Max(1, Width - ModernTheme.Px(this, 3)), Math.Max(1, Height - ModernTheme.Px(this, 3)));
                Color fill = IsRangeBoundary && IsSelectedDate ? ModernTheme.Blue : IsSelectedDate ? Color.FromArgb(38, ModernTheme.Blue) : Enabled && hovered ? ModernTheme.Background : ModernTheme.Surface;
                using (Brush brush = new SolidBrush(fill))
                {
                    if (IsRangeBoundary && IsSelectedDate)
                    {
                        int diameter = Math.Min(rect.Width, rect.Height);
                        e.Graphics.FillEllipse(brush, rect.X + (rect.Width - diameter) / 2, rect.Y, diameter, diameter);
                    }
                    else using (GraphicsPath rounded = ModernTheme.Rounded(rect, ModernTheme.Px(this, 5))) e.Graphics.FillPath(brush, rounded);
                }
                Color text = !Enabled ? ModernTheme.Blend(ModernTheme.Secondary, ModernTheme.Surface, .55) : IsRangeBoundary && IsSelectedDate ? Color.White : IsCurrentMonth ? ModernTheme.Text : ModernTheme.Secondary;
                TextRenderer.DrawText(e.Graphics, Date.Day.ToString(CultureInfo.CurrentCulture), Font, rect, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (Date == DateTime.Today && !IsSelectedDate)
                    using (Pen pen = new Pen(ModernTheme.Blue, ModernTheme.ScaleFactor(this))) e.Graphics.DrawRectangle(pen, rect);
                if (Focused && Enabled) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -ModernTheme.Px(this, 3), -ModernTheme.Px(this, 3)), text, fill);
            }
        }

        private sealed class RailButton : Button
        {
            private readonly bool tasks;
            private bool hovered;
            public bool IsSelected;
            public RailButton(bool tasks)
            {
                this.tasks = tasks;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                MouseEnter += delegate { hovered = true; Invalidate(); };
                MouseLeave += delegate { hovered = false; Invalidate(); };
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(ModernTheme.Surface);
                if (hovered || IsSelected)
                    using (Brush brush = new SolidBrush(ModernTheme.Background)) e.Graphics.FillRectangle(brush, new Rectangle(1, 1, Width - 2, Height - 2));
                Color color = IsSelected ? ModernTheme.Blue : hovered ? ModernTheme.Text : ModernTheme.Secondary;
                GraphicsState iconState = e.Graphics.Save();
                float scale = Math.Min(Width / 38F, Height / 42F);
                e.Graphics.ScaleTransform(scale, scale);
                using (Pen pen = new Pen(color, 1.65f))
                {
                    if (!tasks)
                    {
                        e.Graphics.DrawRectangle(pen, 10, 12, 18, 18);
                        e.Graphics.DrawLine(pen, 10, 17, 28, 17);
                        e.Graphics.DrawLine(pen, 14, 9, 14, 14);
                        e.Graphics.DrawLine(pen, 24, 9, 24, 14);
                        e.Graphics.DrawLine(pen, 14, 22, 16, 22);
                        e.Graphics.DrawLine(pen, 21, 22, 24, 22);
                        e.Graphics.DrawLine(pen, 14, 26, 16, 26);
                    }
                    else
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            int y = 13 + i * 7;
                            e.Graphics.DrawLine(pen, 9, y + 1, 11, y + 3);
                            e.Graphics.DrawLine(pen, 11, y + 3, 15, y - 1);
                            e.Graphics.DrawLine(pen, 19, y + 1, 29, y + 1);
                        }
                    }
                }
                e.Graphics.Restore(iconState);
                int margin = ModernTheme.Px(this, 4);
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(margin, margin, Width - margin * 2, Height - margin * 2), color, ModernTheme.Surface);
            }
        }
    }

    internal sealed class OverlayPanelPreferences
    {
        public int Version { get; set; }
        public bool CalendarPinned { get; set; }
        public bool TasksPinned { get; set; }
    }

    // Pin choices have their own small document so they cannot alter timers or task records.
    internal sealed class OverlayPanelPreferenceStore
    {
        private readonly string root;
        private readonly string path;
        private bool loadFailed;
        public bool CalendarPinned { get; set; }
        public bool TasksPinned { get; set; }
        public string LastError { get; private set; }
        internal string LastFailure { get; private set; }
        public OverlayPanelPreferenceStore(string dataRoot)
        {
            if (String.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A panel preference directory is required.", "dataRoot");
            root = Path.GetFullPath(dataRoot);
            path = Path.Combine(root, "panel-preferences.json");
        }
        public bool Load()
        {
            CalendarPinned = TasksPinned = false;
            LastError = LastFailure = null;
            loadFailed = false;
            if (!File.Exists(path)) return true;
            try
            {
                if (new FileInfo(path).Length > 65536) throw new InvalidDataException("The panel preference file is too large.");
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 65536 };
                OverlayPanelPreferences document = serializer.Deserialize<OverlayPanelPreferences>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null || document.Version != 1) throw new InvalidDataException("The panel preference file has an unsupported format.");
                CalendarPinned = document.CalendarPinned;
                TasksPinned = document.TasksPinned;
                return true;
            }
            catch (Exception ex)
            {
                if (!IsStorageException(ex)) throw;
                loadFailed = true;
                LastFailure = ex.GetType().Name + ": " + ex.Message;
                LastError = "Panel preferences could not be loaded. The original file was kept.";
                return false;
            }
        }
        public bool Save()
        {
            string temporary = Path.Combine(root, ".panel-preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                if (loadFailed && File.Exists(path))
                {
                    LastError = "The saved panel preferences need recovery before pin choices can be saved.";
                    return false;
                }
                Directory.CreateDirectory(root);
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 65536 };
                string json = serializer.Serialize(new OverlayPanelPreferences { Version = 1, CalendarPinned = CalendarPinned, TasksPinned = TasksPinned });
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(json); writer.Flush(); stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
                LastError = LastFailure = null;
                return true;
            }
            catch (Exception ex)
            {
                if (!IsStorageException(ex)) throw;
                LastFailure = ex.GetType().Name + ": " + ex.Message;
                LastError = "Could not save panel preferences. Check access to the data folder.";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        private static bool IsStorageException(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException;
        }
    }

    internal sealed class OverlayTaskItem
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public bool Completed { get; set; }
        public DateTime CreatedUtc { get; set; }
        public OverlayTaskItem Copy() { return new OverlayTaskItem { Id = Id, Title = Title, Category = Category, Completed = Completed, CreatedUtc = CreatedUtc }; }
    }

    internal sealed class OverlayTaskDocument
    {
        public int Version { get; set; }
        public List<OverlayTaskItem> Items { get; set; }
    }

    internal sealed class OverlayTaskStore
    {
        public const int MaximumTasks = 500;
        private readonly string root;
        private readonly string path;
        private bool loadFailed;
        public readonly List<OverlayTaskItem> Items = new List<OverlayTaskItem>();
        public string LastError { get; private set; }
        internal string LastFailure { get; private set; }
        public OverlayTaskStore(string dataRoot)
        {
            if (String.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A task data directory is required.", "dataRoot");
            root = Path.GetFullPath(dataRoot);
            path = Path.Combine(root, "tasks.json");
        }
        public bool Load()
        {
            Items.Clear(); LastError = null; LastFailure = null; loadFailed = false;
            if (!File.Exists(path)) return true;
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Length > 1024 * 1024) throw new InvalidDataException("The task file is too large.");
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                OverlayTaskDocument document = serializer.Deserialize<OverlayTaskDocument>(File.ReadAllText(path, Encoding.UTF8));
                if (document == null || document.Version != 1 || document.Items == null || document.Items.Count > MaximumTasks)
                    throw new InvalidDataException("The task file has an unsupported format.");
                ValidateItems(document.Items);
                Items.AddRange(document.Items);
                return true;
            }
            catch (Exception ex)
            {
                if (!IsFileException(ex) && !(ex is ArgumentException) && !(ex is InvalidOperationException)) throw;
                loadFailed = true;
                LastFailure = ex.GetType().Name + ": " + ex.Message;
                LastError = "Tasks could not be loaded. The original file was kept.";
                return false;
            }
        }
        public bool Save()
        {
            string temporary = Path.Combine(root, ".tasks-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                ValidateItems(Items);
                // Never replace a file that failed to load; recovering it is an explicit action.
                if (loadFailed && File.Exists(path)) { LastError = "The saved task file needs recovery before changes can be saved."; return false; }
                Directory.CreateDirectory(root);
                JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
                string json = serializer.Serialize(new OverlayTaskDocument { Version = 1, Items = Items });
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                    {
                        writer.Write(json); writer.Flush(); stream.Flush(true);
                    }
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
                LastError = null;
                LastFailure = null;
                return true;
            }
            catch (Exception ex)
            {
                if (!IsFileException(ex) && !(ex is ArgumentException) && !(ex is InvalidOperationException)) throw;
                LastFailure = ex.GetType().Name + ": " + ex.Message;
                LastError = "Could not save tasks. Check access to the data folder.";
                return false;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        private static void ValidateItems(IList<OverlayTaskItem> items)
        {
            if (items.Count > MaximumTasks) throw new InvalidDataException("The task file contains too many tasks.");
            HashSet<Guid> ids = new HashSet<Guid>();
            foreach (OverlayTaskItem item in items)
            {
                Guid id;
                if (item == null || !Guid.TryParse(item.Id, out id) || !ids.Add(id) || String.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 160 || !ValidCategory(item.Category))
                    throw new InvalidDataException("A task contains invalid data.");
            }
        }
        private static bool ValidCategory(string category) { return category == "Work" || category == "Game" || category == "Personal"; }
        private static bool IsFileException(Exception ex)
        {
            return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException;
        }
    }
}
