using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace GameManagement
{
    internal sealed class ModernReportsView : UserControl
    {
        private readonly string historyPath;
        private readonly ComboBox period = new ComboBox();
        private readonly DateTimePicker from = new DateTimePicker();
        private readonly DateTimePicker to = new DateTimePicker();
        private readonly TextBox search = new TextBox();
        private readonly DataGridView history = new DataGridView();
        private readonly Label workValue = ModernTheme.Label("0h 00m", 21, true);
        private readonly Label gameValue = ModernTheme.Label("0h 00m", 21, true);
        private readonly Label totalValue = ModernTheme.Label("0h 00m", 21, true);
        private readonly Label cyclesValue = ModernTheme.Label("0", 21, true);
        private readonly Label status = ModernTheme.Label("", 9);
        private readonly ActivityBars chart = new ActivityBars();
        private List<TrackedSession> sessions = new List<TrackedSession>();
        private List<ReportRow> visibleRows = new List<ReportRow>();
        private bool updatingRange;

        internal ModernReportsView(string historyPath)
        {
            this.historyPath = historyPath;
            Dock = DockStyle.Fill;
            BackColor = ModernTheme.Background;
            Font = ModernTheme.Font(9);
            AutoScroll = true;
            Padding = Padding.Empty;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                ColumnCount = 1, RowCount = 6,
                BackColor = ModernTheme.Background, Margin = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 208));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            Panel header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label heading = ModernTheme.Label("Reports", 24, true);
            heading.Location = new Point(0, 0);
            header.Controls.Add(heading);
            Label description = ModernTheme.Label("Understand how you spend your time.", 9);
            description.ForeColor = ModernTheme.Secondary;
            description.Location = new Point(2, 48);
            header.Controls.Add(description);
            Button export = ModernTheme.Button("Export CSV", ExportCsv);
            export.Size = new Size(114, 34);
            export.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(export);
            header.Resize += delegate { export.SetBounds(Math.Max(0, header.Width - ModernTheme.Px(header, 114)), ModernTheme.Px(header, 6), ModernTheme.Px(header, 114), ModernTheme.Px(header, 34)); };
            layout.Controls.Add(header, 0, 0);

            FlowLayoutPanel range = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Margin = Padding.Empty, Padding = new Padding(0, 9, 0, 0)
            };
            period.DropDownStyle = ComboBoxStyle.DropDownList;
            period.AccessibleName = "Report period";
            period.BackColor = ModernTheme.Surface;
            period.ForeColor = ModernTheme.Text;
            period.Size = new Size(104, 28);
            period.Items.AddRange(new object[] { "Daily", "Weekly", "Monthly", "All time", "Custom" });
            period.Margin = new Padding(0, 0, 12, 0);
            range.Controls.Add(period);
            range.Controls.Add(RangeCaption("From"));
            SetupDatePicker(from);
            from.AccessibleName = "Report start date";
            range.Controls.Add(from);
            range.Controls.Add(RangeCaption("To"));
            SetupDatePicker(to);
            to.AccessibleName = "Report end date";
            range.Controls.Add(to);
            Button refresh = ModernTheme.Button("Refresh", RefreshData);
            refresh.Size = new Size(88, 30);
            refresh.Margin = new Padding(12, 0, 0, 0);
            range.Controls.Add(refresh);
            layout.Controls.Add(range, 0, 1);

            TableLayoutPanel metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 12) };
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            metrics.Controls.Add(Metric("Work time", workValue, ModernTheme.Blue), 0, 0);
            metrics.Controls.Add(Metric("Game time", gameValue, ModernTheme.Game), 1, 0);
            metrics.Controls.Add(Metric("Total active time", totalValue, ModernTheme.Text), 2, 0);
            metrics.Controls.Add(Metric("Completed cycles", cyclesValue, ModernTheme.Success), 3, 0);
            layout.Controls.Add(metrics, 0, 2);
            ModernCard graph = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12), Padding = new Padding(16, 10, 16, 8) };
            graph.Controls.Add(chart);
            layout.Controls.Add(graph, 0, 3);

            Panel historyHeading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label historyTitle = ModernTheme.Label("Session history", 13, true);
            historyTitle.Location = new Point(0, 8);
            historyHeading.Controls.Add(historyTitle);
            search.BorderStyle = BorderStyle.FixedSingle;
            search.Size = new Size(220, 25);
            search.BackColor = ModernTheme.Surface;
            search.ForeColor = ModernTheme.Text;
            search.AccessibleName = "Search session history";
            search.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            historyHeading.Controls.Add(search);
            Label searchLabel = ModernTheme.Label("Search", 9);
            searchLabel.ForeColor = ModernTheme.Secondary;
            historyHeading.Controls.Add(searchLabel);
            historyHeading.Resize += delegate
            {
                search.SetBounds(Math.Max(ModernTheme.Px(historyHeading, 160), historyHeading.Width - ModernTheme.Px(historyHeading, 220)), ModernTheme.Px(historyHeading, 6), ModernTheme.Px(historyHeading, 220), ModernTheme.Px(historyHeading, 28));
                searchLabel.Location = new Point(search.Left - ModernTheme.Px(historyHeading, 52), ModernTheme.Px(historyHeading, 10));
            };
            layout.Controls.Add(historyHeading, 0, 4);

            ModernCard historyCard = new ModernCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(1) };
            ConfigureGrid();
            historyCard.Controls.Add(history);
            status.Dock = DockStyle.Bottom;
            status.Height = 28;
            status.Padding = new Padding(10, 6, 0, 0);
            status.ForeColor = ModernTheme.Secondary;
            historyCard.Controls.Add(status);
            layout.Controls.Add(historyCard, 0, 5);

            period.SelectedIndexChanged += delegate { SetPeriod(); };
            from.ValueChanged += delegate { CustomRange(from); };
            to.ValueChanged += delegate { CustomRange(to); };
            search.TextChanged += delegate { FillHistory(); };
            Action arrange = delegate
            {
                Func<int, int> px = delegate(int value) { return ModernTheme.Px(this, value); };
                int height = Math.Max(px(660), ClientSize.Height - px(36));
                int width = Math.Max(px(660), ClientSize.Width - px(48) - (height > ClientSize.Height - px(36) ? SystemInformation.VerticalScrollBarWidth : 0));
                layout.SetBounds(px(24), px(18), width, height);
                int[] rowHeights = { 72, 53, 108, 208, 44 };
                for (int i = 0; i < rowHeights.Length; i++) layout.RowStyles[i].Height = px(rowHeights[i]);
                heading.Location = Point.Empty;
                description.Location = new Point(px(2), heading.PreferredSize.Height + px(4));
                layout.RowStyles[0].Height = Math.Max(px(72), description.Bottom + px(6));
                export.SetBounds(width - px(114), px(6), px(114), px(34));
                range.Padding = new Padding(0, px(9), 0, 0);
                foreach (Control field in range.Controls) field.Margin = new Padding(0, 0, px(12), 0);
                period.Width = px(104); from.Width = to.Width = px(130); refresh.Size = new Size(px(88), px(30));
                metrics.Margin = new Padding(0, 0, 0, px(12));
                graph.Margin = new Padding(0, 0, 0, px(12)); graph.Padding = new Padding(px(16), px(10), px(16), px(8));
                foreach (Control card in metrics.Controls) card.Margin = new Padding(0, 0, px(10), 0);
                historyTitle.Location = new Point(0, px(8));
                status.Height = px(28); status.Padding = new Padding(px(10), px(6), 0, 0);
                history.ColumnHeadersHeight = px(36); history.RowTemplate.Height = px(34);
                foreach (DataGridViewRow row in history.Rows) row.Height = px(34);
                float fontScale = Math.Max(1, FindForm() == null ? Font.SizeInPoints / 9F : FindForm().Font.SizeInPoints / 9F);
                history.DefaultCellStyle.Font = ModernTheme.Font(9 * fontScale);
                history.ColumnHeadersDefaultCellStyle.Font = ModernTheme.Font(9 * fontScale, FontStyle.Bold);
                AutoScrollMinSize = new Size(layout.Right + px(24), layout.Bottom + px(18));
            };
            Resize += delegate { arrange(); };
            FontChanged += delegate { arrange(); };
            heading.FontChanged += delegate { arrange(); };
            description.FontChanged += delegate { arrange(); };
            ParentChanged += delegate { arrange(); };
            arrange();
            period.SelectedIndex = 1;
            RefreshData();
        }

        private static Label RangeCaption(string text)
        {
            Label label = ModernTheme.Label(text, 9);
            label.ForeColor = ModernTheme.Secondary;
            label.Margin = new Padding(0, 6, 8, 0);
            return label;
        }

        private static void SetupDatePicker(DateTimePicker picker)
        {
            picker.Format = DateTimePickerFormat.Custom;
            picker.CustomFormat = "MMM d, yyyy";
            picker.Size = new Size(130, 28);
            picker.Margin = new Padding(0, 0, 12, 0);
            picker.Font = ModernTheme.Font(9);
            picker.CalendarMonthBackground = ModernTheme.Surface;
            picker.CalendarForeColor = ModernTheme.Text;
        }

        private static ModernCard Metric(string title, Label value, Color accent)
        {
            ModernCard card = new ModernCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            Label name = ModernTheme.Label(title, 9);
            name.ForeColor = ModernTheme.Secondary;
            name.Location = new Point(14, 12);
            value.Location = new Point(13, 34);
            value.ForeColor = accent;
            card.Controls.Add(name);
            card.Controls.Add(value);
            card.Resize += delegate
            {
                int left = ModernTheme.Px(card, 14), top = ModernTheme.Px(card, 12);
                name.Location = new Point(left, top);
                value.Location = new Point(ModernTheme.Px(card, 13), ModernTheme.Px(card, 38));
                name.MaximumSize = value.MaximumSize = new Size(Math.Max(40, card.Width - left * 2), 0);
            };
            return card;
        }

        private void ConfigureGrid()
        {
            history.Dock = DockStyle.Fill;
            history.AccessibleName = "Saved session history";
            history.BackgroundColor = ModernTheme.Surface;
            history.BorderStyle = BorderStyle.None;
            history.GridColor = ModernTheme.Border;
            history.AllowUserToAddRows = false;
            history.AllowUserToDeleteRows = false;
            history.AllowUserToResizeRows = false;
            history.ReadOnly = true;
            history.RowHeadersVisible = false;
            history.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            history.MultiSelect = false;
            history.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            history.EnableHeadersVisualStyles = false;
            history.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            history.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            history.ColumnHeadersHeight = 36;
            history.ColumnHeadersDefaultCellStyle.BackColor = ModernTheme.Background;
            history.ColumnHeadersDefaultCellStyle.ForeColor = ModernTheme.Secondary;
            history.ColumnHeadersDefaultCellStyle.Font = ModernTheme.Font(9, FontStyle.Bold);
            history.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            history.DefaultCellStyle.BackColor = ModernTheme.Surface;
            history.DefaultCellStyle.ForeColor = ModernTheme.Text;
            history.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 238, 255);
            history.DefaultCellStyle.SelectionForeColor = ModernTheme.Text;
            history.DefaultCellStyle.Font = ModernTheme.Font(9);
            history.DefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
            history.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            history.RowTemplate.Height = 34;
            AddColumn("Started", 23);
            AddColumn("Mode", 12);
            AddColumn("Application", 36);
            AddColumn("Active time", 18);
            AddColumn("Cycles", 11);
        }

        private void AddColumn(string title, float weight)
        {
            history.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        internal void RefreshData()
        {
            try
            {
                sessions = WeeklyReport.Load(historyPath);
                if (period.SelectedIndex == 3) SetPeriod();
                else BuildReport();
            }
            catch (Exception ex)
            {
                sessions = new List<TrackedSession>();
                BuildReport();
                status.Text = "History could not be read: " + ex.Message;
            }
        }

        internal void SetDateRange(DateTime start, DateTime endInclusive)
        {
            DateTime beginning = (start <= endInclusive ? start : endInclusive).Date;
            DateTime ending = (start <= endInclusive ? endInclusive : start).Date;
            updatingRange = true;
            period.SelectedIndex = 4;
            from.Value = ClampDate(beginning);
            to.Value = ClampDate(ending);
            updatingRange = false;
            RefreshData();
        }

        private void SetPeriod()
        {
            if (updatingRange || period.SelectedIndex == 4) return;
            DateTime start = DateTime.Today;
            DateTime finish = start;
            if (period.SelectedIndex == 1)
            {
                start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
                finish = start.AddDays(6);
            }
            else if (period.SelectedIndex == 2)
            {
                start = new DateTime(start.Year, start.Month, 1);
                finish = start.AddMonths(1).AddDays(-1);
            }
            else if (period.SelectedIndex == 3 && sessions.Count > 0)
            {
                start = sessions.Min(item => item.Start).Date;
                finish = sessions.Max(item => item.End.AddTicks(-1)).Date;
            }
            updatingRange = true;
            from.Value = ClampDate(start);
            to.Value = ClampDate(finish);
            updatingRange = false;
            BuildReport();
        }

        private DateTime ClampDate(DateTime date)
        {
            return date < from.MinDate ? from.MinDate : date > from.MaxDate ? from.MaxDate : date;
        }

        private void CustomRange(DateTimePicker changed)
        {
            if (updatingRange) return;
            updatingRange = true;
            period.SelectedIndex = 4;
            if (from.Value.Date > to.Value.Date)
            {
                if (changed == from) to.Value = from.Value.Date;
                else from.Value = to.Value.Date;
            }
            updatingRange = false;
            BuildReport();
        }

        private void BuildReport()
        {
            DateTime start = from.Value.Date;
            DateTime end = to.Value.Date.AddDays(1);
            visibleRows = new List<ReportRow>();
            TimeSpan work = TimeSpan.Zero, game = TimeSpan.Zero;
            int cycles = 0;
            int dayCount = Math.Max(1, (int)(end - start).TotalDays);
            int bucketCount = Math.Min(14, dayCount);
            List<ActivityBucket> buckets = new List<ActivityBucket>();
            for (int i = 0; i < bucketCount; i++)
            {
                DateTime bucketStart = start.AddDays((long)i * dayCount / bucketCount);
                DateTime bucketEnd = start.AddDays((long)(i + 1) * dayCount / bucketCount);
                buckets.Add(new ActivityBucket { Start = bucketStart, End = bucketEnd });
            }
            foreach (TrackedSession session in sessions.OrderByDescending(item => item.Start))
            {
                TimeSpan active = ActiveInRange(session, start, end);
                if (active <= TimeSpan.Zero) continue;
                bool isWork = string.Equals(session.Mode, "work", StringComparison.OrdinalIgnoreCase);
                if (isWork) work += active; else game += active;
                // Cycles are recorded for a completed session, so count them on its finish date.
                int sessionCycles = session.End > start && session.End <= end ? Math.Max(0, session.Cycles) : 0;
                cycles += sessionCycles;
                visibleRows.Add(new ReportRow { Session = session, Active = active, Cycles = sessionCycles });
                foreach (ActivityBucket bucket in buckets)
                {
                    double seconds = ActiveInRange(session, bucket.Start, bucket.End).TotalSeconds;
                    if (isWork) bucket.WorkSeconds += seconds; else bucket.GameSeconds += seconds;
                }
            }
            workValue.Text = DurationText(work);
            gameValue.Text = DurationText(game);
            totalValue.Text = DurationText(work + game);
            cyclesValue.Text = cycles.ToString(CultureInfo.InvariantCulture);
            chart.SetData(buckets);
            FillHistory();
        }

        // Same interval and legacy checkpoint rules as WeeklyReport.BuildPeriod.
        internal static TimeSpan ActiveInRange(TrackedSession session, DateTime start, DateTime end)
        {
            if (session == null || session.End <= session.Start || session.End <= start || session.Start >= end) return TimeSpan.Zero;
            IEnumerable<SessionInterval> intervals = session.ActiveSegments ?? new List<SessionInterval> { new SessionInterval { Start = session.Start, End = session.End } };
            double scale = session.ActiveSegments == null && session.ActiveSeconds.HasValue
                ? Math.Min(1, Math.Max(0, session.ActiveSeconds.Value) / (session.End - session.Start).TotalSeconds) : 1;
            long ticks = 0;
            foreach (SessionInterval interval in intervals)
            {
                DateTime beginning = interval.Start > start ? interval.Start : start;
                DateTime ending = interval.End < end ? interval.End : end;
                if (ending > beginning) ticks += (long)((ending - beginning).Ticks * scale);
            }
            return TimeSpan.FromTicks(ticks);
        }

        private void FillHistory()
        {
            string query = search.Text.Trim();
            history.Rows.Clear();
            foreach (ReportRow row in FilteredRows(query))
            {
                string mode = string.Equals(row.Session.Mode, "work", StringComparison.OrdinalIgnoreCase) ? "Work" : "Game";
                int index = history.Rows.Add(row.Session.Start.ToString("MMM d, yyyy  HH:mm"), mode,
                    string.IsNullOrWhiteSpace(row.Session.Application) ? "Unknown application" : row.Session.Application,
                    DurationText(row.Active), row.Cycles.ToString(CultureInfo.InvariantCulture));
                history.Rows[index].Cells[1].Style.ForeColor = mode == "Work" ? ModernTheme.Blue : ModernTheme.Game;
            }
            if (history.Rows.Count > 0) history.ClearSelection();
            status.Text = history.Rows.Count == 0 ? "No saved sessions in this range. Sessions over two minutes appear here."
                : history.Rows.Count + (history.Rows.Count == 1 ? " saved session" : " saved sessions") + "  |  Pauses excluded  |  Cycles counted when sessions finish";
        }

        private IEnumerable<ReportRow> FilteredRows(string query)
        {
            return visibleRows.Where(row => query.Length == 0 ||
                (row.Session.Start.ToString("yyyy-MM-dd") + " " + row.Session.Mode + " " + row.Session.Application).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ExportCsv()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export session history";
                dialog.Filter = "CSV file (*.csv)|*.csv";
                dialog.FileName = "GameManagement-report-" + from.Value.ToString("yyyy-MM-dd") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, BuildCsv(), new UTF8Encoding(true));
                    status.Text = "CSV exported: " + Path.GetFileName(dialog.FileName);
                }
                catch (Exception ex) { MessageBox.Show(this, "The CSV could not be saved. " + ex.Message, "Export report", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            }
        }

        internal string BuildCsv()
        {
            StringBuilder csv = new StringBuilder("Started,Finished,Mode,Application,Active seconds in range,Completed cycles,Range start,Range end\r\n");
            foreach (ReportRow row in FilteredRows(search.Text.Trim()))
            {
                csv.Append(Csv(row.Session.Start.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                    .Append(Csv(row.Session.End.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
                    .Append(Csv(row.Session.Mode)).Append(',').Append(Csv(row.Session.Application)).Append(',')
                    .Append(row.Active.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.Cycles.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(from.Value.ToString("yyyy-MM-dd")).Append(',').Append(to.Value.ToString("yyyy-MM-dd")).Append("\r\n");
            }
            return csv.ToString();
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            // Prevent application names from becoming formulas when opened in a spreadsheet.
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string DurationText(TimeSpan time)
        {
            if (time.TotalMinutes > 0 && time.TotalMinutes < 1) return ((int)time.TotalSeconds) + " sec";
            return ((int)time.TotalHours) + "h " + time.Minutes.ToString("00") + "m";
        }

        private sealed class ReportRow
        {
            internal TrackedSession Session;
            internal TimeSpan Active;
            internal int Cycles;
        }

        private sealed class ActivityBucket
        {
            internal DateTime Start;
            internal DateTime End;
            internal double WorkSeconds;
            internal double GameSeconds;
        }

        private sealed class ActivityBars : Control
        {
            private List<ActivityBucket> buckets = new List<ActivityBucket>();
            private readonly ToolTip details = new ToolTip();
            private int currentBucket = -1;
            internal ActivityBars()
            {
                Dock = DockStyle.Fill;
                BackColor = ModernTheme.Surface;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }
            internal void SetData(List<ActivityBucket> value) { buckets = value; currentBucket = -1; details.SetToolTip(this, ""); Invalidate(); }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int left = ModernTheme.Px(this, 43), top = ModernTheme.Px(this, 37);
                int plotWidth = Math.Max(1, Width - ModernTheme.Px(this, 49));
                int index = e.X < left || e.X >= left + plotWidth || e.Y < top || e.Y > Height - ModernTheme.Px(this, 26)
                    ? -1 : (int)((e.X - left) / (float)plotWidth * buckets.Count);
                if (index == currentBucket) return;
                currentBucket = index;
                if (index < 0 || index >= buckets.Count) { details.SetToolTip(this, ""); return; }
                ActivityBucket bucket = buckets[index];
                string range = bucket.Start.ToString("MMM d, yyyy");
                if ((bucket.End - bucket.Start).TotalDays > 1) range += " to " + bucket.End.AddDays(-1).ToString("MMM d, yyyy");
                details.SetToolTip(this, range + Environment.NewLine + "Work: " + DurationText(TimeSpan.FromSeconds(bucket.WorkSeconds))
                    + Environment.NewLine + "Game: " + DurationText(TimeSpan.FromSeconds(bucket.GameSeconds)));
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing) details.Dispose();
                base.Dispose(disposing);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                Func<int, int> px = delegate(int value) { return ModernTheme.Px(this, value); };
                float fontScale = Math.Max(1, FindForm() == null ? Font.SizeInPoints / 9F : FindForm().Font.SizeInPoints / 9F);
                using (Font title = ModernTheme.Font(11 * fontScale, FontStyle.Bold))
                using (Font label = ModernTheme.Font(8 * fontScale))
                using (Brush text = new SolidBrush(ModernTheme.Text))
                using (Brush secondary = new SolidBrush(ModernTheme.Secondary))
                using (Brush work = new SolidBrush(ModernTheme.Blue))
                using (Brush game = new SolidBrush(ModernTheme.Game))
                using (Pen grid = new Pen(ModernTheme.Border))
                {
                    g.DrawString("Activity over time", title, text, 0, 1);
                    int legend = Math.Max(px(180), Width - px(145));
                    g.FillRectangle(work, legend, px(8), px(8), px(8));
                    g.DrawString("Work", label, secondary, legend + px(13), px(3));
                    g.FillRectangle(game, legend + px(68), px(8), px(8), px(8));
                    g.DrawString("Game", label, secondary, legend + px(81), px(3));
                    Rectangle plot = new Rectangle(px(43), px(37), Math.Max(1, Width - px(49)), Math.Max(1, Height - px(63)));
                    double maximum = buckets.Count == 0 ? 0 : buckets.Max(item => item.WorkSeconds + item.GameSeconds);
                    if (maximum <= 0)
                    {
                        g.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
                        TextRenderer.DrawText(g, "Your activity chart will appear after a session is saved.", label, plot, ModernTheme.Secondary,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                        return;
                    }
                    double maxHours = Math.Max(1, Math.Ceiling(maximum / 3600));
                    for (int tick = 0; tick <= 2; tick++)
                    {
                        float y = plot.Bottom - plot.Height * tick / 2F;
                        g.DrawLine(grid, plot.Left, y, plot.Right, y);
                        g.DrawString((maxHours * tick / 2).ToString("0.#") + "h", label, secondary, 0, y - px(7));
                    }
                    float slot = plot.Width / (float)Math.Max(1, buckets.Count);
                    float width = Math.Min(px(44), Math.Max(px(4), slot * 0.52F));
                    for (int i = 0; i < buckets.Count; i++)
                    {
                        ActivityBucket item = buckets[i];
                        float x = plot.Left + i * slot + (slot - width) / 2;
                        float gameHeight = (float)(item.GameSeconds / 3600 / maxHours * plot.Height);
                        float workHeight = (float)(item.WorkSeconds / 3600 / maxHours * plot.Height);
                        if (gameHeight > 0) g.FillRectangle(game, x, plot.Bottom - gameHeight, width, gameHeight);
                        if (workHeight > 0) g.FillRectangle(work, x, plot.Bottom - gameHeight - workHeight, width, workHeight);
                        string date = item.Start.ToString("MMM d");
                        Rectangle dateBounds = new Rectangle((int)(plot.Left + i * slot), plot.Bottom + px(5), (int)slot, px(18));
                        TextRenderer.DrawText(g, date, label, dateBounds, ModernTheme.Secondary, TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
                    }
                }
            }
        }
    }
}
