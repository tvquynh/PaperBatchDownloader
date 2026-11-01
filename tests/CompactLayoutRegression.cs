using PaperBatchDownloader;
using System.ComponentModel;
using System.Reflection;

internal static class CompactLayoutRegression
{
    public static async Task Run()
    {
        string profile = Path.Combine(Path.GetTempPath(), "CompactLayout-" + Guid.NewGuid().ToString("N"));
        using var app = new MainForm(profile) { ShowInTaskbar = false, Opacity = 0 };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object Field(string name) => typeof(MainForm).GetField(name, flags)!.GetValue(app)!;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        app.Show();
        var end = DateTime.UtcNow.AddSeconds(20);
        while (!(bool)Field("initialized"))
        { if (DateTime.UtcNow >= end) throw new TimeoutException("Compact layout init"); await Task.Delay(50); }
        var jobs = (BindingList<DownloadJob>)Field("jobs");
        for (int i = 1; i <= 20; i++) jobs.Add(new DownloadJob { Id = i, Title = "Sample paper " + i, PublisherKey = "Fixture", Status = JobStatus.Pending });
        app.PerformLayout();
        var grid = (DataGridView)Field("grid");
        var split = app.Controls.OfType<SplitContainer>().Single();
        var options = (Control)Field("advancedOptions");
        Check(!options.Visible && !((Control)Field("lblPublisherSummary")).Visible, "Advanced controls are not collapsed");
        Check(grid.DisplayedRowCount(false) == 3, $"Expected three complete rows, got {grid.DisplayedRowCount(false)}; grid={grid.Height}; template={grid.RowTemplate.Height}; header={grid.ColumnHeadersHeight}");
        Check(split.Panel2.Height > app.ClientSize.Height / 2, "Browser did not get most window space");
        using (var image = new Bitmap(app.Width, app.Height))
        { app.DrawToBitmap(image, app.ClientRectangle); image.Save(Path.GetFullPath("tests/compact-v5.3.5.png")); }
        int original = split.SplitterDistance;
        split.SplitterDistance += 100;
        int manuallyExpanded = split.SplitterDistance;
        Check(grid.DisplayedRowCount(false) > 3, "Manual splitter expansion did not enlarge queue");
        ((Button)Field("btnOptions")).PerformClick();
        app.PerformLayout();
        Check(options.Visible && ((NumericUpDown)Field("numDownloadInterval")).Visible && ((CheckBox)Field("chkSaveCheckbox")).Visible, "Options did not expose existing settings");
        Check(split.SplitterDistance == manuallyExpanded, "Options reset user's splitter position");
        ((Button)Field("btnOptions")).PerformClick();
        app.PerformLayout();
        Check(split.SplitterDistance == manuallyExpanded, "Collapsing options reset user's splitter position");
        split.SplitterDistance = original;
        app.Close();
        Console.WriteLine("PASS: compact default controls, exactly three complete queue rows, browser majority, draggable divider, options preserve manual height");
    }
}
