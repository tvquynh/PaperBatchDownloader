using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Collections;
using System.Reflection;
using System.Text;

internal static class LibraryAccessRegression
{
    public static async Task Run()
    {
        string profile = Path.Combine(Path.GetTempPath(), "LibraryAccessRegression-" + Guid.NewGuid().ToString("N"));
        using var app = new MainForm(profile) { ShowInTaskbar = false, Opacity = 0 };
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        object? Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, args);
        string Value(object r, string property) => r.GetType().GetProperty(property)!.GetValue(r)?.ToString() ?? "";
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        app.Show();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!(bool)Field("initialized")) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Library browser init"); await Task.Delay(100); }
        var resources = (IList)Field("libraryResources");
        Check(resources.Count == 14, "Default library resources missing");
        object Resource(string key) => resources.Cast<object>().Single(r => Value(r, "Key") == key);
        string ovid = Value(Resource("ovid"), "LoginUrl");
        Check(Uri.UnescapeDataString(ovid.Split("?url=")[1]) == "https://ovidsp.ovid.com/ovidweb.cgi?T=JS&NEWS=n&CSC=Y&PAGE=main&D=yrovft", "OpenAthens nested URL query was truncated");
        var core = ((WebView2)Field("web")).CoreWebView2;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            string host = new Uri(e.Request.Uri).Host;
            string html = host == "doi.org"
                ? "<html><head><meta name='citation_doi' content='10.1016/library-fixture'></head><body>You have full access to this article. <a href='https://www.sciencedirect.com/science/article/pii/library-fixture/pdfft'>View PDF</a></body></html>"
                : "<html><body>Please sign in through your institution.</body></html>";
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html");
        };
        async Task Load(Action action)
        {
            var loaded = new TaskCompletionSource();
            void Finished(object? s, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
            core.NavigationCompleted += Finished;
            try { action(); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { core.NavigationCompleted -= Finished; }
        }
        var jobs = (IList)Field("jobs");
        var sample = new DownloadJob { Id=1, Doi="10.1016/library-fixture", Url="https://doi.org/10.1016/library-fixture", PublisherKey="Elsevier", Title="Library subscription sample", Status=JobStatus.Pending, IsOpenAccess="false" };
        jobs.Add(sample);
        jobs.Add(new DownloadJob { Id=2, Url="https://ieeexplore.ieee.org/document/1", PublisherKey="IEEE", Status=JobStatus.Pending });
        jobs.Add(new DownloadJob { Id=3, Url="https://dl.acm.org/doi/1", PublisherKey="ACM", Status=JobStatus.Pending });
        Call("RefreshLibraryResources");
        Check(resources.Count == 15 && Value(Resource("elsevier"), "Papers") == "1" && Value(Resource("extra:ACM"), "Papers") == "1", "Library CSV counts/additional publisher mapping failed");
        var sourceGrid = (DataGridView)Field("libraryGrid");
        object elsevier = Resource("elsevier");
        await Load(() => Call("OpenLibraryResource", elsevier));
        Check((bool)Field("manualLoginMode") && (bool)Field("paused") && core.Source.Contains("go.openathens.net/redirector/vnu.edu.vn"), "VNU login did not use manual paused mode");
        await (Task)Call("CheckLibraryPageAsync")!;
        Check(Value(elsevier, "AccessState") == "Cần đăng nhập", "Login page was reported ready");
        // SSO popup windows must use the same browser profile/cookie store.
        var cookie = core.CookieManager.CreateCookie("library_fixture", "shared-profile", "library-fixture.invalid", "/");
        core.CookieManager.AddOrUpdateCookie(cookie);
        await core.ExecuteScriptAsync("window.open('about:blank','library-sso-fixture')");
        Form? popup = null;
        deadline = DateTime.UtcNow.AddSeconds(15);
        while (popup is null)
        {
            popup = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Owner == app);
            if (DateTime.UtcNow > deadline) throw new TimeoutException("SSO popup missing");
            await Task.Delay(50);
        }
        popup.Opacity = 0;
        var popupBrowser = popup.Controls.OfType<WebView2>().Single();
        while (popupBrowser.CoreWebView2 is null)
        { if (DateTime.UtcNow > deadline) throw new TimeoutException("SSO popup browser missing"); await Task.Delay(50); }
        var cookies = await popupBrowser.CoreWebView2.CookieManager.GetCookiesAsync("https://library-fixture.invalid/");
        Check(cookies.Any(c => c.Name == "library_fixture" && c.Value == "shared-profile"), "SSO popup did not share profile cookies");
        popup.Close();
        await Load(() => Call("OpenLibraryResourceForSample", elsevier, sample));
        await (Task)Call("CheckLibraryPageAsync")!;
        Check(Value(elsevier, "AccessState").StartsWith("Trang xác nhận quyền bài mẫu"), "Matching sample access confirmation not detected");
        Check(jobs.Cast<DownloadJob>().All(j => j.Status == JobStatus.Pending && j.Attempts == 0) && (int)Field("currentIndex") == -1, "Access preparation modified download queue");
        typeof(MainForm).GetField("currentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, 0);
        sample.Status = JobStatus.Navigating;
        await (Task)Call("ApplyPageDecisionAsync", sample, false)!;
        Check(sample.Status == JobStatus.Navigating, "An old queue callback analyzed a manual login page");
        sample.Status = JobStatus.Pending;
        typeof(MainForm).GetField("currentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, -1);
        await Load(() => core.NavigateToString("<meta name='citation_doi' content='10.1016/another-article'><body>You have full access to this article.</body>"));
        await (Task)Call("CheckLibraryPageAsync")!;
        Check(!Value(elsevier, "AccessState").StartsWith("Trang xác nhận"), "Another article confirmed the selected sample's access");
        // Persist URL overrides only; access confirmation must not survive restart.
        ((TextBox)Field("txtLibraryUrl")).Text = "https://library.example.test/login";
        Call("SaveSelectedLibraryUrl");
        using (var restored = new MainForm(profile))
        {
            var saved = (IList)typeof(MainForm).GetField("libraryResources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(restored)!;
            object restoredSource = saved.Cast<object>().Single(r => Value(r, "Key") == "elsevier");
            Check(Value(restoredSource, "LoginUrl") == "https://library.example.test/login" && Value(restoredSource, "AccessState") == "Chưa kiểm tra", "Saved URL or fresh-session state incorrect");
        }
        ((TabControl)Field("libraryTabs")).SelectedIndex = 1;
        await Load(() => Call("OpenNextLibraryResource"));
        Check(Value(Field("activeLibraryResource"), "Key") == "scopus", "Next-source action lost the selected source");
        Check(((TextBox)Field("txtLibraryUrl")).Text == Value(Resource("scopus"), "LoginUrl"), "URL editor does not match the selected source");
        app.PerformLayout();
        using var bitmap = new Bitmap(app.Width, app.Height);
        app.DrawToBitmap(bitmap, app.ClientRectangle);
        bitmap.Save(Path.GetFullPath("tests/library-access-v5.2.11.png"));
        app.Close();
        Console.WriteLine("PASS: 14 VNU library routes, nested OpenAthens query, CSV publisher mapping, manual login, sample identity/access check, unchanged queue, URL persistence and next-source navigation");
    }
}
