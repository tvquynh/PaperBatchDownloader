using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Reflection;
using System.Text;

internal static class PaywallRegression
{
    public static async Task Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "PaywallRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var app = new MainForm(Path.Combine(folder, "profile")) { ShowInTaskbar = false, Opacity = 0 };
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        Task Call(string name, params object[] args) => (Task)typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, args)!;
        async Task Until(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Paywall queue did not advance promptly"); await Task.Delay(100); }
        }
        app.Show();
        await Until(() => (bool)Field("initialized"));
        var core = ((WebView2)Field("web")).CoreWebView2;
        core.AddWebResourceRequestedFilter("https://link.springer.com/*", CoreWebView2WebResourceContext.All);
        int lockedPdfRequests = 0;
        core.WebResourceRequested += (_, e) =>
        {
            string path = new Uri(e.Request.Uri).AbsolutePath;
            if (path.StartsWith("/__paper_downloader__/")) return;
            string body, type = "text/html";
            if (path.Contains("locked.pdf")) { lockedPdfRequests++; body = "Locked"; }
            else if (path.EndsWith("entitlement")) body = "Your institution does not subscribe to this content. Please sign in through your institution.";
            else if (path.EndsWith("preview")) body = "This is a preview of subscription content. Please sign in through your institution. <a href='/content/pdf/locked.pdf'>Download PDF</a>";
            else if (path.EndsWith(".pdf")) { body = "%PDF-1.4\naccessible main article\n%%EOF"; type = "application/pdf"; }
            else body = "You have full access to this article. <a href='/content/pdf/available.pdf'>Download PDF</a><footer>Buy this article</footer>";
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)), 200, "OK", "Content-Type: " + type);
        };
        string csv = Path.Combine(folder, "paywall.csv");
        File.WriteAllText(csv, "landing_page,save_as\nhttps://link.springer.com/article/preview,preview.pdf\nhttps://link.springer.com/article/entitlement,entitlement.pdf\nhttps://link.springer.com/article/available,available.pdf\n");
        ((TextBox)Field("txtCsv")).Text = csv;
        ((TextBox)Field("txtFolder")).Text = Path.Combine(folder, "papers");
        ((NumericUpDown)Field("numWaitSeconds")).Value = 600;
        typeof(MainForm).GetMethod("LoadCsv", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);
        var jobs = (System.Collections.IList)Field("jobs");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Call("StartQueueAsync");
        await Until(() => ((DownloadJob)jobs[2]!).Status == JobStatus.Completed);
        if (clock.Elapsed.TotalSeconds >= 20 || lockedPdfRequests != 0
            || jobs.Cast<DownloadJob>().Take(2).Any(j => j.Status != JobStatus.Deferred || j.DeferredReason != "Paywall")
            || !File.ReadAllText(((DownloadJob)jobs[2]!).FilePath).StartsWith("%PDF-"))
            throw new Exception("Paywall waited, attempted a locked candidate, or prevented the next same-publisher download");
        Console.WriteLine($"PASS: explicit paywalls deferred and next same-publisher PDF completed in {clock.Elapsed.TotalSeconds:F1}s with 600s timeout; locked links not requested; full access retained");
        app.Close();
    }
}
