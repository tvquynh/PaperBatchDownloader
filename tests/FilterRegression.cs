using PaperBatchDownloader;
using System.ComponentModel;
using System.Reflection;

internal static class FilterRegression
{
    public static async Task Run()
    {
        string profile = Path.Combine(Path.GetTempPath(), "PaperFilterRegression-" + Guid.NewGuid().ToString("N"));
        using var app = new MainForm(profile) { ShowInTaskbar = false, Opacity = 0 };
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        void Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, args);
        app.Show();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!(bool)Field("initialized"))
        { if (DateTime.UtcNow > deadline) throw new TimeoutException("Filter browser init"); await Task.Delay(100); }
        var grid = (DataGridView)Field("grid");
        grid.BindingContext = new BindingContext();
        var jobs = (BindingList<DownloadJob>)Field("jobs");
        var first = new DownloadJob { Id=1, Title="Benign classification", PublisherKey="Springer", Status=JobStatus.Completed };
        var second = new DownloadJob { Id=2, Title="Malware detection with ML", PublisherKey="IEEE", Status=JobStatus.Deferred };
        var third = new DownloadJob { Id=3, Title="MALWARE analysis", PublisherKey="Springer", Status=JobStatus.Deferred };
        jobs.Add(first); jobs.Add(second); jobs.Add(third);
        Call("SetColumnFilter", "Title", "malware", false);
        if (grid.Rows.Count != 2 || jobs.Count != 3) throw new Exception("Title filter changed queue or failed case-insensitive matching");
        Call("SetColumnFilter", "PublisherKey", "Springer", true);
        if (grid.Rows.Count != 1 || !ReferenceEquals(grid.Rows[0].DataBoundItem, third)) throw new Exception("Combined column filters failed");
        typeof(MainForm).GetField("currentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, 2);
        Call("SelectAndScrollToCurrentRow");
        if (!ReferenceEquals(grid.CurrentRow?.DataBoundItem, third)) throw new Exception("Queue index selected the wrong filtered row");
        Call("SetColumnFilter", "StatusText", "Deferred", true);
        third.Status = JobStatus.Completed;
        jobs.ResetItem(2);
        if (grid.Rows.Count != 0 || jobs.Count != 3) throw new Exception("Filtered status did not update after download completion");
        Call("SelectAndScrollToCurrentRow"); // Hidden active row must be harmless.
        Call("ClearColumnFilters");
        if (grid.Rows.Count != 3 || jobs[2] != third) throw new Exception("Clear filters failed to restore original queue order");
        Call("ShowColumnFilter", 2);
        var menu = (ContextMenuStrip)Field("columnFilterMenu");
        using (var popup = new Bitmap(menu.Width, menu.Height))
        { menu.DrawToBitmap(popup, menu.ClientRectangle); popup.Save(Path.GetFullPath("tests/column-filter-menu-v5.2.9.png")); }
        menu.Close();
        // Reopen and close to check the popup's lifetime across message-loop turns.
        await Task.Delay(100);
        Call("ShowColumnFilter", 2);
        menu.Close();
        await Task.Delay(100);
        Call("SetColumnFilter", "Title", "malware", false);
        app.Size = new Size(1450, 900);
        app.PerformLayout();
        var filterBar = (Control)Field("filterBar");
        filterBar.Parent!.PerformLayout();
        if (!filterBar.Visible || grid.Top < filterBar.Bottom) throw new Exception("Filter summary/clear button overlaps the table");
        using var bitmap = new Bitmap(app.Width, app.Height);
        app.DrawToBitmap(bitmap, app.ClientRectangle);
        bitmap.Save(Path.GetFullPath("tests/column-filters-v5.2.9.png"));
        app.Close();
        Console.WriteLine("PASS: column filters combine without changing queue; selection follows job identity; live status updates and clearing preserve order");
    }
}
