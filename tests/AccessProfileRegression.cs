using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Collections;
using System.Reflection;
using System.Text;

internal static class AccessProfileRegression
{
    public static async Task Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(MainForm app, string name) => typeof(MainForm).GetField(name, flags)!.GetValue(app)!;
        void Set(MainForm app, string name, object value) => typeof(MainForm).GetField(name, flags)!.SetValue(app, value);
        object? Call(MainForm app, string name, params object[] args) => typeof(MainForm).GetMethod(name, flags)!.Invoke(app, args);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        async Task Ready(MainForm app)
        {
            app.Show(); var deadline = DateTime.UtcNow.AddSeconds(25);
            while (!(bool)Field(app, "initialized")) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Profile browser init"); await Task.Delay(100); }
        }
        var id = typeof(MainForm).GetMethod("TryGetIeeeArticleNumber", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var (url, expected) in new[] {
            ("https://ieeexplore.ieee.org/ielx7/1/2/08703786.pdf", "8703786"),
            ("https://ieeexplore.ieee.org/stamp/stamp.jsp?arnumber=09512394", "9512394") })
        {
            object[] args = { url, "" };
            Check((bool)id.Invoke(null, args)! && (string)args[1] == expected, "IEEE padding was not normalized");
        }
        using (var defaultIp = new MainForm(null, AccessMode.PtitIp))
        using (var defaultVnu = new MainForm(null, AccessMode.VnuAccount))
        {
            Check((string)Field(defaultIp, "browserProfilePath") != (string)Field(defaultVnu, "browserProfilePath"), "Production profiles share a browser folder");
            Check(typeof(MainForm).GetProperty("ProfileDataPath", flags)!.GetValue(defaultIp)!.ToString() != typeof(MainForm).GetProperty("ProfileDataPath", flags)!.GetValue(defaultVnu)!.ToString(), "Production profiles share saved queue data");
        }
        string root = Path.Combine(Path.GetTempPath(), "AccessModeRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var ip = new MainForm(Path.Combine(root, "ip"), AccessMode.PtitIp) { Opacity = 0, ShowInTaskbar = false };
        using var vnu = new MainForm(Path.Combine(root, "vnu"), AccessMode.VnuAccount) { Opacity = 0, ShowInTaskbar = false };
        await Ready(ip); await Ready(vnu);
        var ipCore = ((WebView2)Field(ip, "web")).CoreWebView2;
        var vnuCore = ((WebView2)Field(vnu, "web")).CoreWebView2;
        ipCore.CookieManager.AddOrUpdateCookie(ipCore.CookieManager.CreateCookie("profile_test", "ip", "profile.fixture.invalid", "/"));
        vnuCore.CookieManager.AddOrUpdateCookie(vnuCore.CookieManager.CreateCookie("profile_test", "vnu", "profile.fixture.invalid", "/"));
        Check((await ipCore.CookieManager.GetCookiesAsync("https://profile.fixture.invalid/")).Single().Value == "ip", "IP cookie was overwritten by VNU");
        Check((await vnuCore.CookieManager.GetCookiesAsync("https://profile.fixture.invalid/")).Single().Value == "vnu", "VNU cookie was overwritten by IP");
        var publisherJob = new DownloadJob { PublisherKey="Elsevier", Doi="10.1016/fixture", Url="https://doi.org/10.1016/fixture" };
        const string article = "https://www.sciencedirect.com/science/article/pii/FIXTURE";
        object[] routing = { publisherJob, article, "" };
        Check(!(bool)Call(ip, "TryRouteVnuArticle", routing)!, "IP mode routed through VNU");
        routing = new object[] { publisherJob, article, "" };
        Check((bool)Call(vnu, "TryRouteVnuArticle", routing)! && ((string)routing[2]).StartsWith("https://go.openathens.net/redirector/vnu.edu.vn?url="), "VNU article route missing");
        Check(!(bool)Call(vnu, "TryRouteVnuArticle", new object[] { publisherJob, article, "" })!, "VNU routing looped");
        var directJob = new DownloadJob { PublisherKey="Elsevier", IsDirectPdfCandidate=true, Url="https://www.sciencedirect.com/a.pdf" };
        Check((bool)Call(ip, "ShouldUseAnonymousHttp", directJob)! && !(bool)Call(vnu, "ShouldUseAnonymousHttp", directJob)!, "VNU did not retain browser-session transport");

        // Continue must leave manual login mode, then actually download without another click.
        ipCore.AddWebResourceRequestedFilter("https://continue.fixture.invalid/*", CoreWebView2WebResourceContext.All);
        ipCore.WebResourceRequested += (_, e) =>
        {
            if (new Uri(e.Request.Uri).AbsolutePath.StartsWith("/__paper_downloader__/")) return;
            bool pdf = e.Request.Uri.EndsWith(".pdf");
            string body = pdf ? "%PDF-1.4\ncontinue fixture\n%%EOF" : "<html><a href='/main.pdf'>Download PDF</a></html>";
            int status = e.Request.Uri.EndsWith("/throttled") ? 429 : 200;
            e.Response = ipCore.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)), status, status == 200 ? "OK" : "Too Many Requests", "Content-Type: " + (pdf ? "application/pdf" : "text/html"));
        };
        string csv = Path.Combine(root, "continue.csv");
        File.WriteAllText(csv, "landing_page,file_name\nhttps://continue.fixture.invalid/article,continue.pdf\n");
        ((TextBox)Field(ip, "txtCsv")).Text = csv;
        ((TextBox)Field(ip, "txtFolder")).Text = Path.Combine(root, "pdfs");
        Call(ip, "LoadCsv"); Call(ip, "PrepareDownloadFolder");
        var jobs = (IList)Field(ip, "jobs");
        var job = (DownloadJob)jobs[0]!;
        Check(job.SaveAs == "continue.pdf", "file_name CSV alias was lost");
        Set(ip, "currentIndex", 0); Set(ip, "manualLoginMode", true);
        job.Status = JobStatus.ManualAttention;
        var loaded = new TaskCompletionSource();
        void Loaded(object? s, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
        ipCore.NavigationCompleted += Loaded; ipCore.Navigate(job.Url);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)); ipCore.NavigationCompleted -= Loaded;
        await (Task)Call(ip, "ContinueCurrentAsync")!;
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (job.Status != JobStatus.Completed) { if (DateTime.UtcNow > limit) throw new Exception("Continue did not download automatically: " + job.Error); await Task.Delay(100); }
        Check(!(bool)Field(ip, "manualLoginMode"), "Continue retained manual login mode");

        job.Status = JobStatus.Navigating;
        loaded = new TaskCompletionSource();
        ipCore.NavigationCompleted += Loaded;
        ipCore.Navigate("https://continue.fixture.invalid/throttled");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)); ipCore.NavigationCompleted -= Loaded;
        limit = DateTime.UtcNow.AddSeconds(5);
        while ((bool)Call(ip, "CanSchedulePublisher", job.PublisherKey)!)
        { if (DateTime.UtcNow > limit) throw new Exception("HTTP 429 did not back off its publisher"); await Task.Delay(50); }
        Check((bool)Call(ip, "CanSchedulePublisher", "Other-publisher")!, "One throttled publisher blocked every source");
        job.Status = JobStatus.Completed;
        Call(ip, "RecordDownloadCompletion", job, job.FilePath, "throttle-fixture");
        Check(!(bool)Call(ip, "CanSchedulePublisher", job.PublisherKey)!, "A concurrent successful PDF erased the active rate-limit cooldown");

        // Logout requests the official endpoint and clears only the VNU browser state.
        vnuCore.AddWebResourceRequestedFilter("https://login.openathens.net/*", CoreWebView2WebResourceContext.All);
        vnuCore.WebResourceRequested += (_, e) => e.Response = vnuCore.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes("<html>You have been signed out.</html>")), 200, "OK", "Content-Type: text/html");
        await (Task)Call(vnu, "RequestVnuLogoutAsync")!;
        Check((await vnuCore.CookieManager.GetCookiesAsync("https://profile.fixture.invalid/")).Count == 0, "VNU cookies survived logout");
        Check((await ipCore.CookieManager.GetCookiesAsync("https://profile.fixture.invalid/")).Single().Value == "ip", "VNU logout cleared PTIT cookies");
        Check(job.Status == JobStatus.Completed && File.Exists(job.FilePath), "Logout damaged an existing PDF/queue");
        vnu.PerformLayout();
        using (var image = new System.Drawing.Bitmap(vnu.Width, vnu.Height))
        { vnu.DrawToBitmap(image, vnu.ClientRectangle); image.Save(Path.GetFullPath("tests/access-profiles-v5.3.0-test1.png")); }
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(child => new[] { child }.Concat(Descendants(child)));
        void Switch(MainForm app, int modeIndex)
        {
            ((ComboBox)Field(app, "cmbAccessMode")).SelectedIndex = modeIndex;
            Descendants(app).OfType<Button>().Single(b => b.Text == "Đổi hồ sơ").PerformClick();
        }
        string selectedFolder = ((TextBox)Field(ip, "txtFolder")).Text;
        Switch(ip, 1);
        Check(ip.RequestedAccessMode == AccessMode.VnuAccount, "Switch did not request VNU");
        using var switched = new MainForm(Path.Combine(root, "switched-vnu"), ip.RequestedAccessMode!.Value,
            ip.RequestedInputPaths) { Opacity = 0, ShowInTaskbar = false };
        Check(((TextBox)Field(switched, "txtCsv")).Text == csv
            && ((TextBox)Field(switched, "txtFolder")).Text == selectedFolder, "Switch changed CSV/output selections");
        Check(((IList)Field(switched, "jobs")).Count == 0, "Switch carried old profile queue jobs");
        await Ready(switched);
        ((TextBox)Field(switched, "txtCsv")).Text = "";
        ((TextBox)Field(switched, "txtFolder")).Text = "";
        Switch(switched, 0);
        using var returned = new MainForm(Path.Combine(root, "returned-ip"), switched.RequestedAccessMode!.Value,
            switched.RequestedInputPaths);
        Check(((TextBox)Field(returned, "txtCsv")).Text == ""
            && ((TextBox)Field(returned, "txtFolder")).Text == "", "Switch replaced blank selections with defaults");
        Console.WriteLine("PASS: profile switches preserve exact CSV/output fields in both directions, including blanks, without carrying queue jobs");
        vnu.Close();
        Console.WriteLine("PASS: isolated PTIT/VNU cookies and data paths; VNU routing; IEEE zero-padding; file_name import; automatic Continue; VNU logout retains PTIT and PDFs");
    }
}
