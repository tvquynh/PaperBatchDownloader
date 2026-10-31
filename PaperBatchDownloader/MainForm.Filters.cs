using System.ComponentModel;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private sealed record ColumnFilter(string Text, bool Exact);
    private readonly Dictionary<string, ColumnFilter> columnFilters = new();
    private readonly BindingList<DownloadJob> filteredJobs = new();
    private readonly ContextMenuStrip columnFilterMenu = new() { ShowImageMargin = false };
    private readonly Label lblFilterSummary = new() { AutoSize = true, Padding = new Padding(3, 6, 3, 3) };
    private readonly FlowLayoutPanel filterBar = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false };

    private void InitializeColumnFilters(Control parent)
    {
        Disposed += (_, _) => { columnFilterMenu.Close(); columnFilterMenu.Dispose(); };
        var clear = new Button { Text = "Clear all filters", AutoSize = true };
        clear.Click += (_, _) => ClearColumnFilters();
        filterBar.Controls.AddRange(new Control[] { lblFilterSummary, clear });
        parent.Controls.Add(filterBar);
        filterBar.SendToBack(); // Dock the top bar before the fill-sized table.
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.Tag = column.HeaderText;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.HeaderText += " \u25be";
            column.ToolTipText = "Click the header to filter this column";
        }
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.ColumnIndex >= 0) ShowColumnFilter(e.ColumnIndex);
        };
        jobs.ListChanged += (_, e) =>
        {
            if (columnFilters.Count == 0) return;
            if (e.ListChangedType == ListChangedType.ItemChanged && e.NewIndex >= 0 && e.NewIndex < jobs.Count)
            {
                var job = jobs[e.NewIndex];
                int index = filteredJobs.IndexOf(job);
                bool matches = MatchesColumnFilters(job);
                if (matches && index >= 0) filteredJobs.ResetItem(index);
                else if (!matches && index >= 0) filteredJobs.RemoveAt(index);
                else if (matches) RebuildFilteredRows();
                UpdateFilterSummary();
            }
            else RebuildFilteredRows();
        };
    }

    private static string FilterCellValue(DownloadJob job, string property) =>
        typeof(DownloadJob).GetProperty(property)?.GetValue(job)?.ToString() ?? "";

    private bool MatchesColumnFilters(DownloadJob job) => columnFilters.All(entry =>
        entry.Value.Exact
            ? string.Equals(FilterCellValue(job, entry.Key), entry.Value.Text, StringComparison.OrdinalIgnoreCase)
            : FilterCellValue(job, entry.Key).Contains(entry.Value.Text, StringComparison.OrdinalIgnoreCase));

    private void SetColumnFilter(string property, string text, bool exact = false)
    {
        if (!exact && string.IsNullOrWhiteSpace(text)) columnFilters.Remove(property);
        else columnFilters[property] = new ColumnFilter(text, exact);
        RebuildFilteredRows();
    }

    private void ClearColumnFilters()
    {
        columnFilters.Clear();
        RebuildFilteredRows();
    }

    private void RebuildFilteredRows()
    {
        var selected = grid.CurrentRow?.DataBoundItem as DownloadJob;
        if (columnFilters.Count == 0)
        {
            grid.DataSource = jobs;
            filteredJobs.Clear();
        }
        else
        {
            filteredJobs.RaiseListChangedEvents = false;
            try
            {
                filteredJobs.Clear();
                foreach (var job in jobs.Where(MatchesColumnFilters)) filteredJobs.Add(job);
            }
            finally { filteredJobs.RaiseListChangedEvents = true; }
            if (!ReferenceEquals(grid.DataSource, filteredJobs)) grid.DataSource = filteredJobs;
            else filteredJobs.ResetBindings();
        }
        if (selected is not null)
        {
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.DataBoundItem, selected));
            if (row is not null) grid.CurrentCell = row.Cells[0];
        }
        foreach (DataGridViewColumn column in grid.Columns)
        {
            string label = column.Tag?.ToString() ?? column.DataPropertyName;
            bool active = columnFilters.TryGetValue(column.DataPropertyName, out var filter);
            column.HeaderText = label + (active ? " \u25cf \u25be" : " \u25be");
            column.ToolTipText = active ? $"{label}: {(filter!.Exact ? "equals" : "contains")} {filter.Text}" : "Click the header to filter this column";
        }
        UpdateFilterSummary();
    }

    private void UpdateFilterSummary()
    {
        filterBar.Visible = columnFilters.Count > 0;
        lblFilterSummary.Text = $"Showing {filteredJobs.Count:N0} / {jobs.Count:N0} papers | {columnFilters.Count} active filter(s) | Filters affect the table only";
    }

    private void ShowColumnFilter(int columnIndex)
    {
        var column = grid.Columns[columnIndex];
        string property = column.DataPropertyName;
        var menu = columnFilterMenu;
        menu.Close();
        foreach (ToolStripItem item in menu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        menu.Items.Add(new ToolStripLabel($"Filter: {column.Tag}"));
        var search = new ToolStripTextBox { AutoSize = false, Width = 280, ToolTipText = "Contains text (case-insensitive). Press Enter to apply." };
        if (columnFilters.TryGetValue(property, out var existing)) search.Text = existing.Text;
        menu.Items.Add(new ToolStripLabel("Contains text:"));
        menu.Items.Add(search);
        void Apply() { SetColumnFilter(property, search.Text); menu.Close(); }
        menu.Items.Add("Apply filter", null, (_, _) => Apply());
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Apply(); }
            else if (e.KeyCode == Keys.Escape) menu.Close();
        };
        menu.Items.Add("Clear this column", null, (_, _) => { columnFilters.Remove(property); RebuildFilteredRows(); });
        menu.Items.Add("Clear all filters", null, (_, _) => ClearColumnFilters());
        // Short categorical columns also offer exact-value selection, including empty cells.
        if (property is "PublisherKey" or "StatusText" or "UrlSource")
        {
            menu.Items.Add(new ToolStripSeparator());
            var values = jobs.Select(j => FilterCellValue(j, property)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            foreach (string value in values)
            {
                var item = new ToolStripMenuItem(value.Length == 0 ? "(Empty)" : value)
                { Checked = existing is { Exact: true } && string.Equals(existing.Text, value, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => SetColumnFilter(property, value, exact: true);
                menu.Items.Add(item);
            }
        }
        var rect = grid.GetCellDisplayRectangle(columnIndex, -1, true);
        menu.Show(grid, new Point(rect.Left, rect.Bottom));
        search.Focus();
    }
}
