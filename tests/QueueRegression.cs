using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Reflection;
using System.Text;

internal static class QueueRegression
{
    public static async Task Run()
    {
        var csvReader = typeof(MainForm).Assembly.GetType("PaperBatchDownloader.CsvUtils")!.GetMethod("ReadJobs")!;
        var samples = (List<DownloadJob>)csvReader.Invoke(null, new object[] { Path.GetFullPath("oa_sciencedirect_test.csv") })!;
        if (samples.Count != 10 || samples.Any(j => j.Status == JobStatus.NoUrl) || samples.Select(j => j.SaveAs).Distinct().Count() != 10
            || samples[8].Url != "https://doi.org/10.1016/j.procs.2024.03.018"
            || !samples[6].Url.Contains("/pdfft") || samples.Count(j => j.PublisherKey == "Elsevier") != 5)
            throw new Exception("Live sample CSV lost article URLs, DOI route, PDF route, or unique filenames");
        Console.WriteLine("PASS: ten live sample CSV rows preserve landing/DOI/PDF routes and unique save_as names");
        var temp = Path.Combine(Path.GetTempPath(), "PaperQueueRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        using var app = new MainForm(Path.Combine(temp, "profile")) { ShowInTaskbar = false, Opacity = 0 };
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        async Task Until(Func<bool> condition, int seconds = 20)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline) throw new Exception("Queue timeout: " + ((Label)Field("lblCurrent")).Text);
                await Task.Delay(100);
            }
        }
        app.Show();
        await Until(() => (bool)Field("initialized"));
        var core = ((WebView2)Field("web")).CoreWebView2;
        var diagnosticLoaded = new TaskCompletionSource();
        void DiagnosticLoaded(object? sender, CoreWebView2NavigationCompletedEventArgs e) => diagnosticLoaded.TrySetResult();
        core.NavigationCompleted += DiagnosticLoaded;
        core.NavigateToString("<html><title>Diagnostic fixture</title><body>Visible diagnostic evidence<input type='password' value='secret-not-for-log'><a href='https://fixture.invalid/sample.pdf'>Download PDF</a></body></html>");
        await diagnosticLoaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        core.NavigationCompleted -= DiagnosticLoaded;
        await (Task)typeof(MainForm).GetMethod("SaveWebViewSnapshotAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null)!;
        string diagnosticFolder = (string)typeof(MainForm).GetProperty("DiagnosticDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        var snapshot = File.ReadAllText(Directory.GetFiles(diagnosticFolder, "webview-*.json").Single());
        if (!snapshot.Contains("Visible diagnostic evidence") || !snapshot.Contains("https://fixture.invalid/sample.pdf") || snapshot.Contains("secret-not-for-log"))
            throw new Exception("WebView snapshot lost visible text/PDF links or captured input values");
        Console.WriteLine("PASS: WebView snapshot captures visible text and PDF links without password input values");
        core.AddWebResourceRequestedFilter("https://ieeexplore.ieee.org/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://doi.org/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://fixture.invalid/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://emerald-fixture.invalid/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://www.sciencedirect.com/*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://link.springer.com/*", CoreWebView2WebResourceContext.All);
        int fontResponses = 0;
        int retryPdfRequests = 0;
        core.WebResourceRequested += (_, e) =>
        {
            var uri = new Uri(e.Request.Uri);
            if (uri.AbsolutePath.StartsWith("/__paper_downloader__/")) return;
            string body, headers;
            int status = 200;
            if (uri.AbsolutePath == "/favicon.ico")
            { body = ""; headers = "Content-Type: image/x-icon"; status = 404; }
            else if (uri.Host == "link.springer.com" && uri.AbsolutePath.EndsWith("retry.pdf"))
            {
                retryPdfRequests++;
                if (retryPdfRequests == 1) { body="Not found";headers="Content-Type: text/html";status=404; }
                else { body="%PDF-1.4\nretry fixture\n%%EOF";headers="Content-Type: application/pdf"; }
            }
            else if (uri.Host == "doi.org")
            { body = ""; headers = "Location: https://ieeexplore.ieee.org/document/10068497"; status = 302; }
            else if (uri.AbsolutePath == "/document/10068497")
            {
                body = "<html><body><a>Personal Sign In</a><a>Institutional Sign In</a><h1>Open Access Article</h1>" +
                    "<iframe src='https://disqus.com/embed/comments/'></iframe><a href='/stamp/stamp.jsp?tp=&amp;arnumber=10068497'>PDF</a></body></html>";
                headers = "Content-Type: text/html";
            }
            else if (uri.Query.Contains("99999999"))
            { body = "<html><body>Document unavailable</body></html>"; headers = "Content-Type: text/html"; }
            else if (uri.AbsolutePath == "/stamp/stamp.jsp")
            { body = "<html><iframe src='/ielx7/1/2/10068497.pdf'></iframe></html>"; headers = "Content-Type: text/html"; }
            else if (uri.AbsolutePath == "/science/article/pii/font-fixture")
            {
                body = "<html><body>ScienceDirect article<a href='/science/article/pii/OTHERARTICLE/pdfft'>View PDF</a><script>fetch('/shared-assets/national.woff2').then(r=>r.arrayBuffer()).then(()=>setTimeout(()=>{let a=document.createElement('a');a.href='/science/article/pii/font-fixture/pdfft';a.innerHTML='View&nbsp;PDF';document.body.appendChild(a)},1000));</script></body></html>";
                headers = "Content-Type: text/html";
            }
            else if (uri.AbsolutePath == "/shared-assets/national.woff2")
            { fontResponses++; body = "wOF2-not-a-PDF"; headers = "Content-Type: application/octet-stream"; }
            else if (uri.AbsolutePath.EndsWith("/pdfft"))
            { body = "<html><body>Redirecting to PDF<script>location.href='/science/article/pii/font-fixture/main.pdf'</script></body></html>"; headers = "Content-Type: text/html"; }
            else if (uri.AbsolutePath == "/denied.pdf")
            { body = "<html><body>Please sign in to access this PDF</body></html>"; headers = "Content-Type: text/html"; }
            else if (uri.Host == "emerald-fixture.invalid")
            { body = "%PDF-1.4\ninline publisher fixture\n%%EOF"; headers = "Content-Type: application/pdf"; }
            else if (uri.Host == "fixture.invalid")
            { body = "%PDF-1.4\nfixture\n%%EOF"; headers = "Content-Type: application/octet-stream\r\nContent-Disposition: attachment; filename=fixture.pdf"; }
            else if (uri.AbsolutePath.EndsWith(".pdf"))
            { body = "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"; headers = "Content-Type: application/pdf"; }
            else { body = "<html>fixture</html>"; headers = "Content-Type: text/html"; }
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)), status, status == 302 ? "Found" : "OK", headers);
        };
        var csv = Path.Combine(temp, "queue.csv");
        File.WriteAllText(csv, "pdf_url\nhttps://doi.org/10.1109/access.2023.3256979\nhttps://ieeexplore.ieee.org/stamp/stamp.jsp?tp=&arnumber=10068497\nhttps://ieeexplore.ieee.org/stamp/stamp.jsp?arnumber=99999999\nhttps://fixture.invalid/download\nhttps://emerald-fixture.invalid/article-pdf/test.pdf\nhttps://emerald-fixture.invalid/inline\nhttps://fixture.invalid/robots.txt\nhttps://emerald-fixture.invalid/denied.pdf\nhttps://www.sciencedirect.com/science/article/pii/font-fixture\n");
        File.AppendAllText(csv,"https://link.springer.com/content/pdf/10.1007/fixture-retry.pdf\n");
        ((TextBox)Field("txtCsv")).Text = csv;
        ((TextBox)Field("txtFolder")).Text = Path.Combine(temp, "papers");
        ((NumericUpDown)Field("numWaitSeconds")).Value = 15;
        typeof(MainForm).GetMethod("LoadCsv", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        var jobs = (System.Collections.IList)Field("jobs");
        var grid = (DataGridView)Field("grid");
        typeof(DataGridView).GetMethod("OnCellMouseDown", BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(DataGridViewCellMouseEventArgs) }, null)!.Invoke(grid,
            new object[] { new DataGridViewCellMouseEventArgs(5, 4, 0, 0, new MouseEventArgs(MouseButtons.Right, 1, 0, 0, 0)) });
        if (grid.CurrentRow?.Index != 4 || grid.CurrentCell?.ColumnIndex != 5 || grid.ContextMenuStrip?.Items.Count < 8)
            throw new Exception("Right-click copy menu did not select the clicked row/cell");
        Console.WriteLine("PASS: right-click selects the clicked row and cell for DOI/URL copying");
        // Exercise the browser fallback directly without making any external HTTP
        // requests to the fake publisher hostname.
        foreach (var j in jobs.Cast<DownloadJob>()) j.IsDirectPdfCandidate = false;
        var start = (Task)typeof(MainForm).GetMethod("StartQueueAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null)!;
        await start.WaitAsync(TimeSpan.FromSeconds(20));
        await Until(() => jobs.Cast<DownloadJob>().All(j => j.Status is JobStatus.Completed or JobStatus.Deferred or JobStatus.Skipped), 75);
        if (jobs.Count != 10 || jobs.Cast<DownloadJob>().Count(j => j.Status == JobStatus.Completed) != 7 ||
            jobs.Cast<DownloadJob>().Where(j => j.Status == JobStatus.Completed).Any(j => !File.ReadAllText(j.FilePath).StartsWith("%PDF-")))
            throw new Exception("Queue did not save seven PDFs, defer unavailable/login PDFs, and skip robots.txt");
        if(retryPdfRequests < 2) throw new Exception("Interrupted native PDF did not retry through browser-session fetch");
        Console.WriteLine("PASS: interrupted native download releases pending state and browser-session fetch saves the PDF");
        var scienceDirect = jobs.Cast<DownloadJob>().Single(j => j.Url.Contains("font-fixture"));
        if (fontResponses == 0 || scienceDirect.Status != JobStatus.Completed || !scienceDirect.PdfUrl.EndsWith("/main.pdf"))
            throw new Exception("ScienceDirect octet-stream font was confused with its real PDF");
        Console.WriteLine("PASS: ScienceDirect ignores fonts/reference PDFs, recognizes nonbreaking-space View PDF, and follows HTML pdfft redirect to real PDF");
        if (jobs.Cast<DownloadJob>().Single(j => j.Url.EndsWith("/robots.txt")).PageDecision != "Skipped: robots.txt")
            throw new Exception("robots.txt was not skipped immediately");
        Console.WriteLine("PASS: full MainForm DOI -> article -> stamp -> iframe -> PDF -> next row, with login header and Disqus iframe");
        Console.WriteLine("PASS: unchanged unavailable page is deferred after 15s and next publisher completes");
        Console.WriteLine("PASS: pdf_url CSV import, generic publisher direct/inline PDFs saved through native downloads, robots.txt skipped");
        var denied = jobs.Cast<DownloadJob>().Single(j => j.Url.EndsWith("/denied.pdf"));
        if (denied.Status != JobStatus.Deferred || denied.FilePath != "") throw new Exception("Login HTML was accepted as a PDF");
        Console.WriteLine("PASS: native download login HTML is rejected, shown for manual attention, then deferred");
        var events = File.ReadLines(Path.Combine(diagnosticFolder, "events.jsonl")).Select(line => System.Text.Json.JsonDocument.Parse(line)).ToList();
        var scienceDirectEvents = events.Where(e => e.RootElement.GetProperty("job").ValueKind == System.Text.Json.JsonValueKind.Object
            && e.RootElement.GetProperty("job").GetProperty("Id").GetInt32() == scienceDirect.Id).ToList();
        if (scienceDirectEvents.Any(e => e.RootElement.GetProperty("job").GetProperty("PdfUrl").GetString()!.Contains(".woff2")
                || e.RootElement.GetProperty("job").GetProperty("PdfUrl").GetString()!.Contains("OTHERARTICLE"))
            || scienceDirectEvents.Count(e => e.RootElement.GetProperty("kind").GetString() == "navigation-start") > 10)
            throw new Exception("ScienceDirect font selected as PDF or download context navigation loop detected");
        if (!events.Any(e => e.RootElement.GetProperty("kind").GetString() == "page-analysis") ||
            !events.Any(e => e.RootElement.GetProperty("kind").GetString() == "download-state"))
            throw new Exception("Missing page/download diagnostic events");
        foreach (var e in events) e.Dispose();
        Console.WriteLine("PASS: parseable per-session page analysis and download-state log");
        await Task.Delay(1200);
        app.Close();
    }
}
