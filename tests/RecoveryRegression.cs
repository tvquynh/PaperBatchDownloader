using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class RecoveryRegression
{
    public static async Task Run()
    {
        string temp = Path.Combine(Path.GetTempPath(), "PdfRecoveryRegression-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(temp, "profile");
        Directory.CreateDirectory(temp);
        using var app = new MainForm(profile) { ShowInTaskbar = false, Opacity = 0 };
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        void Set(string name, object value) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, value);
        Task Call(string name, params object[] args) => (Task)typeof(MainForm).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args)!;
        app.Show();
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!(bool)Field("initialized")) { if (DateTime.UtcNow > limit) throw new TimeoutException("Recovery browser init"); await Task.Delay(100); }
        var core = ((WebView2)Field("web")).CoreWebView2;
        core.AddWebResourceRequestedFilter("https://link.springer.com/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(Encoding.UTF8.GetBytes("<html><head><meta name='citation_doi' content='10.1007/recovery-test'><meta name='citation_pdf_url' content='https://link.springer.com/content/pdf/10.1007/recovery-test.pdf'></head><body>Open article<a href='https://files.eric.ed.gov/fulltext/ED448443.pdf'>Download PDF</a><a href='https://static-content.springer.com/esm/art%3A10.1007%2Frecovery-test/MediaObjects/MOESM1_ESM.pdf'>Download PDF</a></body></html>")), 200, "OK", "Content-Type: text/html");
        async Task Load(Action navigate)
        {
            var loaded = new TaskCompletionSource();
            void Finished(object? s, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
            core.NavigationCompleted += Finished;
            try { navigate(); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { core.NavigationCompleted -= Finished; }
        }
        async Task<object> Analyze()
        {
            var task = Call("AnalyzeCurrentPageAsync"); await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!;
        }
        await Load(() => core.Navigate("https://link.springer.com/article/10.1007/recovery-test"));
        object page = await Analyze();
        var candidates = (List<string>)page.GetType().GetProperty("PdfCandidates")!.GetValue(page)!;
        if (candidates.Count != 1 || !candidates[0].EndsWith("10.1007/recovery-test.pdf"))
            throw new Exception("Springer references were accepted as the current article PDF");
        Console.WriteLine("PASS: Springer DOI matching excludes unrelated reference PDFs");

        await Load(() => core.NavigateToString("<html><body>This is a preview of subscription content, log in via an institution to check access. <a href='https://link.springer.com/content/pdf/10.1007/paywalled.pdf'>Download PDF</a></body></html>"));
        page = await Analyze();
        if ((string)page.GetType().GetProperty("Classification")!.GetValue(page)! != "Paywall")
            throw new Exception("Springer subscription preview was treated as an available PDF");
        Console.WriteLine("PASS: Springer subscription preview is identified before trying its PDF link");

        await Load(() => core.NavigateToString("<html><body>Verify you are human. CAPTCHA. Buy this article.</body></html>"));
        page = await Analyze();
        if ((string)page.GetType().GetProperty("Classification")!.GetValue(page)! != "BotCheck")
            throw new Exception("CAPTCHA was mistaken for a paywall");
        await Load(() => core.NavigateToString("<html><body>Please sign in to access this PDF.</body></html>"));
        page = await Analyze();
        if ((string)page.GetType().GetProperty("Classification")!.GetValue(page)! != "LoginRequired")
            throw new Exception("Login-only page was mistaken for a paywall");
        await Load(() => core.NavigateToString("<html><body>Access denied.</body></html>"));
        page = await Analyze();
        if ((string)page.GetType().GetProperty("Classification")!.GetValue(page)! != "AccessRequired")
            throw new Exception("Ambiguous access denial was mistaken for a confirmed paywall");
        Console.WriteLine("PASS: CAPTCHA, login-only and ambiguous access pages retain their attention classifications");

        await Load(() => core.NavigateToString("<html><body>Your institution does not subscribe to this content on ScienceDirect.</body></html>"));
        page = await Analyze();
        if ((string)page.GetType().GetProperty("Classification")!.GetValue(page)! != "EntitlementDenied")
            throw new Exception("Explicit no-subscription notice was not recognized");
        var jobs = (System.Collections.IList)Field("jobs");
        var denied = new DownloadJob { Id=1, Doi="10.1016/denied", Url="https://doi.org/10.1016/denied", PublisherKey="Elsevier", Status=JobStatus.Navigating };
        var next = new DownloadJob { Id=2, Doi="10.1016/next", Url="https://doi.org/10.1016/next", PublisherKey="Elsevier", Status=JobStatus.Pending };
        jobs.Add(denied); jobs.Add(next);
        Set("currentIndex", 0); Set("paused", true);
        await Call("ApplyPageDecisionAsync", denied, false);
        if (denied.Status != JobStatus.Deferred || denied.DeferredReason != "Paywall" || next.Status != JobStatus.Pending)
            throw new Exception("One article's entitlement denial suspended other Elsevier articles");
        Console.WriteLine("PASS: no-subscription article defers immediately without a timer tick or suspending other publisher rows");

        string pdf = Path.Combine(temp, "late.pdf");
        File.WriteAllText(pdf, "%PDF-1.4\nlate valid download fixture\n%%EOF");
        denied.FilePath = pdf; Set("currentIndex", 1); next.Status = JobStatus.Navigating;
        await Call("FinishDownloadAsync", denied, pdf, true);
        if (denied.Status != JobStatus.Completed || (int)Field("currentIndex") != 1 || next.Status != JobStatus.Navigating)
            throw new Exception("Late completion failed or advanced the current queue row");
        denied.Status=JobStatus.Deferred; denied.FilePath=Path.Combine(temp,"incomplete.pdf");
        File.WriteAllText(denied.FilePath,"%PDF-1.4\nincomplete");
        await Call("FinishDownloadAsync", denied, denied.FilePath, true);
        if (denied.Status != JobStatus.Deferred) throw new Exception("Incomplete background PDF accepted");
        Console.WriteLine("PASS: late complete PDF updates only its owner; incomplete PDF remains deferred");

        Set("currentIndex", -1);
        string csv=Path.Combine(temp,"restore.csv");
        string appendix=Path.Combine(temp,"appendix.pdf");
        File.WriteAllText(appendix,"%PDF-1.4\nwrong supplementary PDF\n%%EOF");
        File.WriteAllText(csv,"doi,save_as\n10.1007/recovery-test,late.pdf\n10.1007/other-test,appendix.pdf\n");
        Directory.CreateDirectory(Path.Combine(profile,"QueueState"));
        File.WriteAllText(Path.Combine(profile,"QueueState","state_v51.json"),JsonSerializer.Serialize(new {
            CsvPath=csv, Jobs=new[]{new {Id=1,Doi="10.1007/recovery-test",Status=JobStatus.Deferred,
                FilePath=pdf,PdfUrl="https://link.springer.com/content/pdf/10.1007/recovery-test.pdf",PageDecision="Deferred: download watchdog timeout"},
                new {Id=2,Doi="10.1007/other-test",Status=JobStatus.Completed,
                FilePath=appendix,PdfUrl="https://static-content.springer.com/esm/art%3A10.1007%2Fother-test/MediaObjects/MOESM1_ESM.pdf",PageDecision="Completed"}}}));
        ((TextBox)Field("txtCsv")).Text=csv;
        ((TextBox)Field("txtFolder")).Text=temp;
        typeof(MainForm).GetMethod("LoadCsv",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,null);
        if (((DownloadJob)jobs[0]!).Status != JobStatus.Completed)
            throw new Exception("Complete deferred PDF did not recover from saved state");
        Console.WriteLine("PASS: saved deferred Springer download recovers only with exact filename, DOI and complete PDF marker");
        var wrong=(DownloadJob)jobs[1]!;
        if(wrong.Status != JobStatus.Deferred || wrong.DeferredReason != "Previous PDF is not the main Springer article")
            throw new Exception("Previously saved supplementary PDF was accepted as the main article");
        typeof(MainForm).GetMethod("PrepareDownloadFolder",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,null);
        bool preserved=(bool)typeof(MainForm).GetMethod("PreserveUntrustedSavedPdf",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(app,new object[]{wrong})!;
        var diagnosticFolder=(string)typeof(MainForm).GetProperty("DiagnosticDirectory",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(app)!;
        if(!preserved || File.Exists(appendix) || Directory.GetFiles(diagnosticFolder,"non-main-*.pdf").Length!=1)
            throw new Exception("Wrong PDF was not preserved before retry");
        Console.WriteLine("PASS: saved supplementary PDFs are deferred and preserved separately before retry");
        app.Close();
    }
}
