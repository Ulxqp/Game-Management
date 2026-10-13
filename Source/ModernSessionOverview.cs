using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace GameManagement
{
    // A native summary of recorded sessions; it shares the Reports interval rules.
    internal sealed class ModernSessionOverview : UserControl
    {
        private readonly string historyPath;
        private readonly ModernCard frame = new ModernCard();
        private readonly ComboBox period = new ComboBox();
        private readonly Label heading = ModernTheme.Label("Session Statistics", 10, true);
        private readonly Label headingIcon = ModernTheme.Label("\uE9D9", 14);
        private readonly Label historyHeading = ModernTheme.Label("Session history", 9, true);
        private readonly Label status = ModernTheme.Label("No saved sessions in this range.", 8);
        private readonly Label empty = ModernTheme.Label("Saved sessions will appear here.", 8);
        private readonly DataGridView history = new DataGridView();
        private readonly Button reports;
        private readonly SessionMetric workCount = new SessionMetric("Work sessions", "\uE821", ModernTheme.Success);
        private readonly SessionMetric workTime = new SessionMetric("Total work time", "\uE916", ModernTheme.Blue);
        private readonly SessionMetric gameCount = new SessionMetric("Game sessions", "\uE7FC", ModernTheme.Game);
        private readonly SessionMetric gameTime = new SessionMetric("Total game time", "\uE916", ModernTheme.Game);
        private List<TrackedSession> sessions = new List<TrackedSession>();
        private DateTime rangeStart = DateTime.Today, rangeEnd = DateTime.Today;
        private DateTime lastStamp = DateTime.MinValue;
        private long lastLength = -1;
        private bool loaded, updatingRange, reading, refreshQueued;
        private int readVersion;
        private volatile bool disposed;

        internal event Action ViewReportsRequested;
        internal DateTime RangeStart { get { return rangeStart; } }
        internal DateTime RangeEnd { get { return rangeEnd; } }

        internal ModernSessionOverview(string historyPath)
        {
            this.historyPath = historyPath;
            BackColor = ModernTheme.Background;
            Font = ModernTheme.Font(9);
            AutoScroll = true;
            MinimumSize = new Size(300, 0);
            Controls.Add(frame);
            headingIcon.Font = new Font("Segoe MDL2 Assets", 14);
            headingIcon.ForeColor = ModernTheme.Blue;
            headingIcon.AutoSize = false;
            heading.AutoSize = false;
            heading.AutoEllipsis = true;
            frame.Controls.Add(headingIcon);
            frame.Controls.Add(heading);
            period.DropDownStyle = ComboBoxStyle.DropDownList;
            period.DrawMode = DrawMode.OwnerDrawFixed;
            period.ItemHeight = 28;
            period.Items.AddRange(new object[] { "Today", "Week", "Month", "All", "Custom" });
            period.Font = ModernTheme.Font(9);
            period.BackColor = ModernTheme.Surface;
            period.ForeColor = ModernTheme.Text;
            period.AccessibleName = "Session statistics date range";
            period.DrawItem += DrawPeriod;
            frame.Controls.Add(period);
            foreach (SessionMetric metric in new[] { workCount, workTime, gameCount, gameTime }) frame.Controls.Add(metric);
            workCount.Value = gameCount.Value = "0";
            workTime.Value = gameTime.Value = "00:00:00";
            frame.Controls.Add(historyHeading);
            ModernTheme.Grid(history);
            history.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            history.AccessibleName = "Recent saved session history";
            history.AllowUserToResizeRows = false;
            history.ColumnHeadersHeight = 34;
            history.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            history.RowTemplate.Height = 32;
            history.DefaultCellStyle.Font = ModernTheme.Font(9);
            history.DefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            history.ColumnHeadersDefaultCellStyle.Font = ModernTheme.Font(9, FontStyle.Bold);
            history.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
            history.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            AddColumn("Mode", 19, 74);
            AddColumn("Start time", 24, 106);
            AddColumn("End time", 24, 106);
            AddColumn("Active time", 26, 94);
            AddColumn("#", 7, 32);
            history.Columns[4].DisplayIndex = 0;
            history.Columns[4].DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
            history.Columns[4].HeaderCell.Style.Padding = Padding.Empty;
            for (int column = 0; column < history.Columns.Count; column++)
            {
                history.Columns[column].DefaultCellStyle.Alignment = column == 0 ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleCenter;
                history.Columns[column].HeaderCell.Style.Alignment = column == 0 ? DataGridViewContentAlignment.MiddleLeft : DataGridViewContentAlignment.MiddleCenter;
            }
            history.CellPainting += PaintHistoryMode;
            frame.Controls.Add(history);
            empty.AutoSize = false;
            empty.TextAlign = ContentAlignment.MiddleCenter;
            empty.ForeColor = ModernTheme.Secondary;
            empty.BackColor = ModernTheme.Surface;
            empty.Padding = new Padding(6);
            frame.Controls.Add(empty);
            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = ModernTheme.Secondary;
            frame.Controls.Add(status);
            reports = ModernTheme.Button("View reports", delegate { Action requested = ViewReportsRequested; if (requested != null) requested(); });
            reports.Font = ModernTheme.Font(8);
            reports.ForeColor = ModernTheme.Blue;
            frame.Controls.Add(reports);
            period.SelectedIndexChanged += delegate { ChangePeriod(); };
            Resize += delegate { LayoutOverview(); };
            FontChanged += delegate { LayoutOverview(); };
            ParentChanged += delegate { LayoutOverview(); };
            HandleCreated += delegate { RefreshData(); };
            period.SelectedIndex = 0;
            LayoutOverview();
        }

        private void AddColumn(string title, float weight, int minimum)
        {
            history.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight, MinimumWidth = minimum, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        private void DrawPeriod(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            Rectangle text = Rectangle.Inflate(e.Bounds, -ModernTheme.Px(this, 8), 0);
            TextRenderer.DrawText(e.Graphics, Convert.ToString(period.Items[e.Index], CultureInfo.CurrentCulture), period.Font, text,
                (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : ModernTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            e.DrawFocusRectangle();
        }

        private void PaintHistoryMode(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            TrackedSession session = history.Rows[e.RowIndex].Tag as TrackedSession;
            if (session == null) return;
            bool work = string.Equals(session.Mode, "work", StringComparison.OrdinalIgnoreCase);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            DataGridView.HitTestInfo hit = history.HitTest(history.PointToClient(Control.MousePosition).X, history.PointToClient(Control.MousePosition).Y);
            using (Brush fill = new SolidBrush(selected ? ModernTheme.Tint(ModernTheme.Blue, .08) : hit.RowIndex == e.RowIndex ? ModernTheme.Tint(ModernTheme.Blue, .025) : ModernTheme.Surface))
                e.Graphics.FillRectangle(fill, e.CellBounds);
            int dotSize = ModernTheme.Px(this, 8), padding = ModernTheme.Px(this, 12);
            using (Brush dot = new SolidBrush(work ? ModernTheme.Success : ModernTheme.Game))
                e.Graphics.FillEllipse(dot, e.CellBounds.Left + padding, e.CellBounds.Top + (e.CellBounds.Height - dotSize) / 2, dotSize, dotSize);
            Rectangle text = new Rectangle(e.CellBounds.Left + padding + dotSize + ModernTheme.Px(this, 8), e.CellBounds.Top,
                Math.Max(1, e.CellBounds.Width - padding - dotSize - ModernTheme.Px(this, 12)), e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, work ? "Work" : "Game", e.CellStyle.Font, text, ModernTheme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            using (Pen divider = new Pen(ModernTheme.Border))
                e.Graphics.DrawLine(divider, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            e.Handled = true;
        }

        internal void SetRange(DateTime from, DateTime toInclusive)
        {
            rangeStart = (from <= toInclusive ? from : toInclusive).Date;
            rangeEnd = (from <= toInclusive ? toInclusive : from).Date;
            updatingRange = true;
            period.SelectedIndex = 4;
            updatingRange = false;
            FillSummary();
            RefreshData();
        }

        private void ChangePeriod()
        {
            if (updatingRange || period.SelectedIndex < 0) return;
            if (period.SelectedIndex == 4) { ChooseCustomRange(); return; }
            SetPresetRange();
            FillSummary();
            RefreshData();
        }

        private void SetPresetRange()
        {
            if (period.SelectedIndex == 4) return;
            DateTime today = DateTime.Today;
            rangeStart = rangeEnd = today;
            if (period.SelectedIndex == 1)
            {
                rangeStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                rangeEnd = rangeStart.AddDays(6);
            }
            else if (period.SelectedIndex == 2)
            {
                rangeStart = new DateTime(today.Year, today.Month, 1);
                rangeEnd = rangeStart.AddMonths(1).AddDays(-1);
            }
            else if (period.SelectedIndex == 3 && sessions.Count > 0)
            {
                rangeStart = sessions.Min(item => item.Start).Date;
                rangeEnd = sessions.Max(item => item.End.AddTicks(-1)).Date;
            }
        }

        private void ChooseCustomRange()
        {
            using (Form dialog = new Form { Text = "Session date range", ClientSize = new Size(336, 170), BackColor = ModernTheme.Background, Font = ModernTheme.Font(9), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false })
            {
                Label fromLabel = ModernTheme.Label("From", 9, true), toLabel = ModernTheme.Label("To", 9, true);
                fromLabel.Location = new Point(16, 20); toLabel.Location = new Point(16, 64);
                dialog.Controls.Add(fromLabel); dialog.Controls.Add(toLabel);
                DateTimePicker fromPicker = RangePicker(rangeStart), toPicker = RangePicker(rangeEnd);
                fromPicker.Location = new Point(76, 16); toPicker.Location = new Point(76, 60);
                dialog.Controls.Add(fromPicker); dialog.Controls.Add(toPicker);
                Button cancel = ModernTheme.Button("Cancel", delegate { dialog.DialogResult = DialogResult.Cancel; });
                Button apply = ModernTheme.Button("Apply range", delegate { dialog.DialogResult = DialogResult.OK; }, true);
                cancel.SetBounds(76, 112, 108, 36); apply.SetBounds(192, 112, 128, 36);
                dialog.Controls.Add(cancel); dialog.Controls.Add(apply); dialog.AcceptButton = apply; dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) == DialogResult.OK) SetRange(fromPicker.Value, toPicker.Value);
                else FillSummary();
            }
        }

        private static DateTimePicker RangePicker(DateTime value)
        {
            DateTimePicker picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "MMM d, yyyy", Width = 244, AccessibleName = "Session date" };
            picker.Value = value < picker.MinDate ? picker.MinDate : value > picker.MaxDate ? picker.MaxDate : value;
            return picker;
        }

        // Ordinary refreshes coalesce file reads on a worker. Forced reads are deterministic
        // for isolated preview capture and tests before a Windows handle has been created.
        internal void RefreshData(bool force = false)
        {
            if (disposed || IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(delegate { RefreshData(force); })); } catch (InvalidOperationException) { }
                return;
            }
            DateTime previousStart = rangeStart, previousEnd = rangeEnd;
            SetPresetRange();
            if (rangeStart != previousStart || rangeEnd != previousEnd) FillSummary();
            if (force)
            {
                readVersion++;
                ApplyRead(ReadHistory(true, lastStamp, lastLength));
                return;
            }
            if (!IsHandleCreated) return;
            if (reading) { refreshQueued = true; return; }
            reading = true;
            int version = readVersion;
            DateTime knownStamp = lastStamp;
            long knownLength = lastLength;
            bool firstRead = !loaded;
            ThreadPool.QueueUserWorkItem(delegate
            {
                HistoryRead result = ReadHistory(firstRead, knownStamp, knownLength);
                if (disposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        reading = false;
                        if (disposed) return;
                        if (version == readVersion) ApplyRead(result);
                        bool again = refreshQueued;
                        refreshQueued = false;
                        if (again) RefreshData();
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private HistoryRead ReadHistory(bool force, DateTime knownStamp, long knownLength)
        {
            try
            {
                FileInfo file = new FileInfo(historyPath);
                DateTime stamp = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue;
                long length = file.Exists ? file.Length : 0;
                if (!force && stamp == knownStamp && length == knownLength) return new HistoryRead();
                return new HistoryRead { Sessions = WeeklyReport.Load(historyPath), Stamp = stamp, Length = length };
            }
            catch (Exception ex) { return new HistoryRead { Error = ex.Message }; }
        }

        private void ApplyRead(HistoryRead result)
        {
            if (result.Error != null)
            {
                status.Text = "History unavailable. Open Reports to retry.";
                status.AccessibleDescription = result.Error;
                return;
            }
            if (result.Sessions == null) return;
            sessions = result.Sessions;
            lastStamp = result.Stamp;
            lastLength = result.Length;
            loaded = true;
            status.AccessibleDescription = "Active time excludes recorded pauses.";
            SetPresetRange();
            FillSummary();
        }

        private void FillSummary()
        {
            DateTime exclusiveEnd = rangeEnd == DateTime.MaxValue.Date ? DateTime.MaxValue : rangeEnd.AddDays(1);
            TimeSpan work = TimeSpan.Zero, game = TimeSpan.Zero;
            int workSessions = 0, gameSessions = 0;
            List<SummaryRow> rows = new List<SummaryRow>();
            foreach (TrackedSession session in sessions.OrderByDescending(item => item.Start))
            {
                TimeSpan active = ModernReportsView.ActiveInRange(session, rangeStart, exclusiveEnd);
                if (active <= TimeSpan.Zero) continue;
                bool isWork = string.Equals(session.Mode, "work", StringComparison.OrdinalIgnoreCase);
                if (isWork) { work += active; workSessions++; } else { game += active; gameSessions++; }
                if (rows.Count < 60) rows.Add(new SummaryRow { Session = session, Active = active, Work = isWork });
            }
            workCount.Value = workSessions.ToString(CultureInfo.InvariantCulture);
            gameCount.Value = gameSessions.ToString(CultureInfo.InvariantCulture);
            workTime.Value = Duration(work);
            gameTime.Value = Duration(game);
            bool today = rangeStart == DateTime.Today && rangeEnd == DateTime.Today;
            workCount.Context = gameCount.Context = period.SelectedIndex == 3 ? "Total sessions" : "Sessions in range";
            workTime.Context = today ? "Today's work time" : "Work in range";
            gameTime.Context = today ? "Today's game time" : "Game in range";
            history.Rows.Clear();
            bool showDate = rangeStart != rangeEnd;
            string format = showDate ? "MMM d HH:mm" : "HH:mm:ss";
            foreach (SummaryRow row in rows)
            {
                int index = history.Rows.Add(row.Work ? "Work" : "Game", row.Session.Start.ToString(format), row.Session.End.ToString(format), Duration(row.Active), history.Rows.Count + 1);
                history.Rows[index].Tag = row.Session;
                history.Rows[index].Cells[3].ToolTipText = "Active time in the selected range; recorded pauses are excluded.";
            }
            if (history.Rows.Count > 0) history.ClearSelection();
            empty.Visible = rows.Count == 0;
            int total = workSessions + gameSessions;
            status.Text = total == 0 ? "No saved sessions in this range."
                : total > rows.Count ? "Latest " + rows.Count + " of " + total + " saved sessions · Pauses excluded"
                : total + (total == 1 ? " saved session" : " saved sessions") + " · Pauses excluded";
            foreach (SessionMetric metric in new[] { workCount, workTime, gameCount, gameTime }) metric.Invalidate();
        }

        private static string Duration(TimeSpan duration)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", (long)duration.TotalHours, duration.Minutes, duration.Seconds);
        }

        private void LayoutOverview()
        {
            if (frame == null || reports == null) return;
            Func<int, int> px = delegate(int value) { return ModernTheme.Px(this, value); };
            bool stacked = ClientSize.Width < px(500);
            int contentHeight = px(stacked ? 366 : 296);
            int width = Math.Max(px(280), ClientSize.Width - (ClientSize.Height < contentHeight ? SystemInformation.VerticalScrollBarWidth : 0));
            frame.SetBounds(0, 0, width, Math.Max(contentHeight, ClientSize.Height));
            headingIcon.SetBounds(px(16), px(14), px(24), px(24));
            period.ItemHeight = px(28);
            period.SetBounds(width - px(116), px(10), px(100), px(34));
            heading.Text = width < px(380) ? "Statistics" : "Session Statistics";
            heading.AccessibleName = "Session Statistics";
            heading.SetBounds(px(46), px(16), Math.Max(px(60), period.Left - px(58)), px(22));
            SessionMetric[] metrics = { workCount, workTime, gameCount, gameTime };
            int columns = stacked ? 2 : 4;
            int metricWidth = Math.Max(px(60), (width - px(32) - (columns - 1) * px(8)) / columns);
            for (int i = 0; i < metrics.Length; i++) metrics[i].SetBounds(px(16) + (i % columns) * (metricWidth + px(8)), px(56) + (i / columns) * px(70), metricWidth, px(62));
            int titleTop = px(stacked ? 196 : 126);
            historyHeading.SetBounds(px(16), titleTop, width - px(32), px(20));
            int historyTop = titleTop + px(24);
            int footerTop = frame.Height - px(34);
            history.SetBounds(px(16), historyTop, width - px(32), Math.Max(px(98), footerTop - historyTop - px(8)));
            history.ColumnHeadersHeight = px(34);
            history.RowTemplate.Height = px(32);
            foreach (DataGridViewRow row in history.Rows) row.Height = px(32);
            float fontScale = Math.Max(1, FindForm() == null ? Font.SizeInPoints / 9F : FindForm().Font.SizeInPoints / 9F);
            period.Font = ModernTheme.Font(9 * fontScale);
            history.DefaultCellStyle.Font = ModernTheme.Font(9 * fontScale);
            history.ColumnHeadersDefaultCellStyle.Font = ModernTheme.Font(9 * fontScale, FontStyle.Bold);
            for (int i = 0; i < history.Columns.Count; i++) history.Columns[i].MinimumWidth = px(new[] { 74, 106, 106, 94, 32 }[i]);
            empty.SetBounds(px(20), historyTop + history.ColumnHeadersHeight + 1, width - px(40), Math.Max(px(28), history.Height - history.ColumnHeadersHeight - px(4)));
            reports.SetBounds(width - px(112), footerTop, px(96), px(30));
            status.SetBounds(px(16), footerTop + px(4), Math.Max(px(100), reports.Left - px(24)), px(22));
            AutoScrollMinSize = new Size(0, contentHeight);
            // Recompute scroll extents after shrinking the frame; otherwise WinForms can
            // retain the previous wide frame and leave a stale horizontal scrollbar.
            PerformLayout();
        }

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            readVersion++;
            base.Dispose(disposing);
        }

        private sealed class HistoryRead
        {
            internal List<TrackedSession> Sessions;
            internal DateTime Stamp;
            internal long Length;
            internal string Error;
        }
        private sealed class SummaryRow
        {
            internal TrackedSession Session;
            internal TimeSpan Active;
            internal bool Work;
        }
        private sealed class SessionMetric : ModernCard
        {
            internal string Value = "0";
            internal string Context = "Sessions in range";
            private readonly string caption, glyph;
            private readonly Color accent;
            internal SessionMetric(string caption, string glyph, Color accent)
            {
                this.caption = caption; this.glyph = glyph; this.accent = accent;
                AccessibleName = caption;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Func<int, int> px = delegate(int value) { return ModernTheme.Px(this, value); };
                float fontScale = Math.Max(1, FindForm() == null ? Font.SizeInPoints / 9F : FindForm().Font.SizeInPoints / 9F);
                using (Font name = ModernTheme.Font(8 * fontScale))
                using (Font number = ModernTheme.Font((Width < px(110) ? 10 : 12) * fontScale, FontStyle.Bold))
                using (Font context = ModernTheme.Font(8 * fontScale))
                {
                    ModernTheme.DrawIcon(e.Graphics, glyph, new Rectangle(px(12), px(7), px(16), px(16)), accent);
                    TextRenderer.DrawText(e.Graphics, caption, name, new Rectangle(px(32), px(7), Math.Max(1, Width - px(40)), px(16)), accent, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, Value, number, new Rectangle(px(12), px(23), Math.Max(1, Width - px(24)), px(22)), ModernTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, Context, context, new Rectangle(px(12), px(44), Math.Max(1, Width - px(24)), px(14)), ModernTheme.Secondary, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    AccessibleDescription = Value + ". " + Context + ".";
                }
            }
        }
    }
}
