using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form { ShowInTaskbar = false, Opacity = 0, Width = 400, Height = 300 };
        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        form.Shown += async (_, _) =>
        {
            try
            {
                if (args.Contains("--validate-new-oa"))
                {
                    string path = Path.GetFullPath("new_oa_test/NEW_OA_150_3_each.csv");
                    var reader = typeof(MainForm).Assembly.GetType("PaperBatchDownloader.CsvUtils")!.GetMethod("ReadJobs")!;
                    var jobs = (List<DownloadJob>)reader.Invoke(null, new object[] { path })!;
                    Check(jobs.Count == 150 && jobs.All(j => j.Status == JobStatus.Pending), "New OA CSV lost records or usable URLs");
                    Check(jobs.Select(j => j.SaveAs).Distinct().Count() == 150, "New OA CSV has colliding filenames");
                    var dois = jobs.Where(j => !string.IsNullOrWhiteSpace(j.Doi)).Select(j => j.Doi).ToList();
                    Check(dois.Distinct(StringComparer.OrdinalIgnoreCase).Count() == dois.Count, "New OA CSV has duplicate DOIs");
                    var report = new { rows=jobs.Count, missingUrl=jobs.Count(j=>j.Status==JobStatus.NoUrl), distinctOutputNames=150,
                        withDoi=dois.Count, routes=jobs.GroupBy(j=>j.UrlSource).ToDictionary(g=>g.Key,g=>g.Count()),
                        publishers=jobs.GroupBy(j=>j.PublisherKey).ToDictionary(g=>g.Key,g=>g.Count()), liveDownloadsVerified=false };
                    File.WriteAllText("new_oa_test/import-validation.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented=true }));
                    Console.WriteLine(JsonSerializer.Serialize(report));
                }
                else if (args.Contains("--compact-only")) await CompactLayoutRegression.Run();
                else if (args.Contains("--save-exit-only")) await SaveExitRegression.Run();
                else if (args.Contains("--filters-only")) await FilterRegression.Run();
                else if (args.Contains("--paywall-only")) await PaywallRegression.Run();
                else if (args.Contains("--library-only")) await LibraryAccessRegression.Run();
                else if (args.Contains("--profiles-only")) await AccessProfileRegression.Run();
                else if (args.Contains("--interval-only")) await PipelineRegression.RunInterval();
                else if (args.Contains("--pipeline-only")) await PipelineRegression.Run();
                else if (args.Contains("--shared-queue-only")) await SharedQueueRegression.Run();
                else if (args.Contains("--publisher-block-only")) await PublisherBlockRegression.Run();
                else if (args.Contains("--inspect-sciencedirect")) await InspectScienceDirect.Run(web);
                else if (args.Contains("--live-sciencedirect")) await InspectScienceDirect.Download();
                else if (args.Contains("--live-springer")) await InspectScienceDirect.Download(springer:true);
                else { await SharedQueueRegression.Run(); await PublisherBlockRegression.Run(); await AccessProfileRegression.Run(); await PipelineRegression.Run(); await LibraryAccessRegression.Run(); await FilterRegression.Run(); await PaywallRegression.Run(); await Run(web); await RecoveryRegression.Run(); await QueueRegression.Run(); Console.WriteLine("PASS: all IEEE regression checks"); }
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { form.Close(); }
        };
        Application.Run(form);
    }

    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    static async Task Run(WebView2 web)
    {
        var temp = Path.Combine(Path.GetTempPath(), "PaperDownloaderRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, Path.Combine(temp, "profile")));
        var core = web.CoreWebView2;
        core.AddWebResourceRequestedFilter("https://ieeexplore.ieee.org/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var uri = new Uri(e.Request.Uri);
            string body, type;
            if (uri.AbsolutePath.EndsWith(".pdf"))
            { body = "%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF"; type = "application/octet-stream"; }
            else if (uri.AbsolutePath == "/stamp/stamp.jsp")
            { body = "<html><iframe src='/ielx7/1/2/11023590.pdf'></iframe></html>"; type = "text/html"; }
            else if (uri.AbsolutePath == "/stamp/stamppdf.jsp")
            { body = "<html><object data='/ielx7/1/2/11023590.pdf'></object></html>"; type = "text/html"; }
            else if (uri.AbsolutePath == "/bad")
            { body = "<html>Login required</html>"; type = "application/pdf"; }
            else
            { body = "<html><body>Authenticated article fixture</body></html>"; type = "text/html"; }
            e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)), 200, "OK", "Content-Type: " + type);
        };
        var loaded = new TaskCompletionSource();
        core.NavigationCompleted += (_, _) => loaded.TrySetResult();
        core.Navigate("https://ieeexplore.ieee.org/document/11023590");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        TaskCompletionSource<string>? completion = null;
        core.DownloadStarting += (_, e) =>
        {
            Console.WriteLine("DownloadStarting: " + e.DownloadOperation.Uri);
            e.Handled = true;
            e.ResultFilePath = Path.Combine(temp, Guid.NewGuid() + ".pdf");
            var path = e.ResultFilePath;
            var waiter = completion;
            Console.WriteLine("Initial download state: " + e.DownloadOperation.State);
            e.DownloadOperation.StateChanged += (_, _) =>
            {
                Console.WriteLine("Download state: " + e.DownloadOperation.State);
                if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed) waiter?.TrySetResult(path);
                if (e.DownloadOperation.State == CoreWebView2DownloadState.Interrupted) waiter?.TrySetException(new Exception("Download interrupted"));
            };
        };
        async Task<string> Evaluate(string url)
        {
            var raw = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new {
                expression = BrowserPdfScript.Create(url, "test.pdf", 5), awaitPromise = true, returnByValue = true, userGesture = true }));
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("result").GetProperty("value").GetString()!;
        }
        string[] ids = "10068497 11023590 11359230 11036813 10830491 10734108 11122514 9130686 9250472 10414983 10843706 11126023 11666856 10937028 11683823 10942324 10806701 11050425 11406109 11715678".Split(' ');
        var isPdf = typeof(MainForm).GetMethod("IsIeeePdfUrl", BindingFlags.NonPublic | BindingFlags.Static)!;
        var article = typeof(MainForm).GetMethod("TryGetIeeeArticleNumber", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var id in ids)
        {
            var url = "https://ieeexplore.ieee.org/stamp/stamp.jsp?tp=&arnumber=" + id;
            Check((bool)isPdf.Invoke(null, new object[] { url })!, "Stamp not recognized: " + id);
            object[] args = { url, "" };
            Check((bool)article.Invoke(null, args)! && (string)args[1] == id, "Wrong article: " + id);
            completion = new();
            loaded = new TaskCompletionSource();
            core.Navigate("https://ieeexplore.ieee.org/document/" + id);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Console.WriteLine("Testing " + id);
            Check(await Evaluate(url) == "OK", "Stamp fetch failed: " + id);
            var path = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(File.ReadAllText(path).StartsWith("%PDF-"), "Invalid saved PDF");
        }
        Console.WriteLine("PASS: all 20 stamp URLs recognized and downloaded using simulated IEEE HTML/PDF responses");
        foreach (var suffix in new[] { "/stamp/stamppdf.jsp?arnumber=11023590", "/ielx7/1/2/11023590.pdf" })
        {
            loaded = new TaskCompletionSource();
            core.Navigate("https://ieeexplore.ieee.org/document/11023590?variant=" + Guid.NewGuid());
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            completion = new();
            Check(await Evaluate("https://ieeexplore.ieee.org" + suffix) == "OK", "PDF variant failed");
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Check((await Evaluate("https://ieeexplore.ieee.org/bad")).StartsWith("ERR:NOT_PDF"), "HTML mislabeled as PDF accepted");
        Check(!(bool)isPdf.Invoke(null, new object[] { "https://fakeieeexplore.ieee.org/1.pdf" })!, "Invalid IEEE hostname accepted");
        Console.WriteLine("PASS: iframe/object resolution, binary signature checks, Promise completion and WebView2 file downloads");
    }
}
