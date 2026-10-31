using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Collections;
using System.Reflection;
using System.Text;

internal static class PublisherBlockRegression
{
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "PublisherBlockRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var app = new MainForm(Path.Combine(root,"profile")) { Opacity=0,ShowInTaskbar=false };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(string name) => typeof(MainForm).GetField(name,flags)!.GetValue(app)!;
        object? Call(string name,params object[] args) => typeof(MainForm).GetMethod(name,flags)!.Invoke(app,args);
        async Task Until(Func<bool> ready)
        {
            var limit = DateTime.UtcNow.AddSeconds(20);
            while(!ready()) { if(DateTime.UtcNow>limit)throw new Exception("Block regression timeout: "+((Label)Field("lblCurrent")).Text); await Task.Delay(50); }
        }
        app.Show(); await Until(()=>(bool)Field("initialized"));
        var core=((WebView2)Field("web")).CoreWebView2;
        core.AddWebResourceRequestedFilter("https://www.sciencedirect.com/*",CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://block.fixture.invalid/*",CoreWebView2WebResourceContext.All);
        int blockedPdfRequests=0,otherScienceDirectArticles=0;
        core.WebResourceRequested+=(_,e)=>
        {
            string path=new Uri(e.Request.Uri).AbsolutePath;
            if(path.StartsWith("/__paper_downloader__/"))return;
            string html,type="text/html";
            if(path.EndsWith("/pdfft")){blockedPdfRequests++;html="%PDF-1.4\nforbidden candidate\n%%EOF";type="application/pdf";}
            else if(path.EndsWith("/UNATTEMPTED")){otherScienceDirectArticles++;html="<html>Should not be requested</html>";}
            else if(path.EndsWith("/BLOCKED"))html="<html><body>There was a problem providing the content you requested. Reference number: fixture. IP Address: 192.0.2.1 CPE00002 <a href='/science/article/pii/BLOCKED/pdfft'>View PDF</a></body></html>";
            else if(path.EndsWith("/QUOTED"))html="<html><body>You have full access to this article. This article quotes: There was a problem providing the content you requested. Reference number: example CPE00002</body></html>";
            else if(path.EndsWith(".pdf")){html="%PDF-1.4\nother source main article\n%%EOF";type="application/pdf";}
            else html="<html><a href='https://block.fixture.invalid/main.pdf'>Download PDF</a></html>";
            e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),200,"OK","Content-Type: "+type);
        };
        string csv=Path.Combine(root,"block.csv");
        File.WriteAllText(csv,"landing_page,doi,publisher,save_as\nhttps://www.sciencedirect.com/science/article/pii/BLOCKED,10.1016/block,Elsevier,blocked.pdf\nhttps://www.sciencedirect.com/science/article/pii/UNATTEMPTED,10.1016/unattempted,Elsevier,unattempted.pdf\nhttps://block.fixture.invalid/article,,Fixture,other.pdf\n");
        ((TextBox)Field("txtCsv")).Text=csv;
        ((TextBox)Field("txtFolder")).Text=Path.Combine(root,"pdfs");
        ((NumericUpDown)Field("numWaitSeconds")).Value=600;
        Call("LoadCsv");
        var jobs=(IList)Field("jobs");
        var clock=System.Diagnostics.Stopwatch.StartNew();
        await(Task)Call("StartQueueAsync")!;
        await Until(()=>!(bool)Field("queueRunning"));
        if(((DownloadJob)jobs[0]!).Status!=JobStatus.Deferred || !((DownloadJob)jobs[0]!).DeferredReason.Contains("CPE00002")
            || ((DownloadJob)jobs[1]!).Status!=JobStatus.Deferred || ((DownloadJob)jobs[1]!).Attempts!=0
            || ((DownloadJob)jobs[2]!).Status!=JobStatus.Completed || blockedPdfRequests!=0 || otherScienceDirectArticles!=0 || clock.Elapsed.TotalSeconds>=20)
            throw new Exception("CPE block did not stop that publisher before links/next rows while letting another source finish");
        typeof(MainForm).GetField("manualLoginMode",flags)!.SetValue(app,true);
        var loaded=new TaskCompletionSource();
        void Loaded(object? s,CoreWebView2NavigationCompletedEventArgs e)=>loaded.TrySetResult();
        core.NavigationCompleted+=Loaded;core.Navigate("https://www.sciencedirect.com/science/article/pii/QUOTED");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));core.NavigationCompleted-=Loaded;
        var analyze=(Task)Call("AnalyzeCurrentPageAsync")!;await analyze;
        object page=analyze.GetType().GetProperty("Result")!.GetValue(analyze)!;
        if(page.GetType().GetProperty("Classification")!.GetValue(page)!.ToString()=="PublisherBlocked")
            throw new Exception("A full-access article quoting an error was falsely suspended");
        app.Close();
        Console.WriteLine("PASS: HTTP-200 ScienceDirect CPE00002 immediately suspends its pending rows without following PDF links; other publisher completes; full-access quote is not blocked");
    }
}
