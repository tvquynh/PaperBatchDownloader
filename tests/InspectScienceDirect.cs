using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json;
using System.Reflection;
using PaperBatchDownloader;

internal static class InspectScienceDirect
{
    public static async Task Download(bool springer = false)
    {
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader", "WebView2ProfileV51");
        string folder = Path.GetFullPath(Path.Combine("tests", (springer ? "live-springer-" : "live-download-") + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(folder);
        using var app = new MainForm(profile) { ShowInTaskbar = false, Opacity = 0 };
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        app.Show();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!(bool)Field("initialized"))
        { if (DateTime.UtcNow > deadline) throw new TimeoutException("WebView init"); await Task.Delay(100); }
        string csv = Path.Combine(folder, "live.csv");
        File.WriteAllText(csv, "landing_page,doi,title,save_as\nhttps://www.sciencedirect.com/science/article/pii/S2352484724001094,10.1016/j.egyr.2024.02.027,Live ScienceDirect OA,live_oa.pdf\nhttps://www.sciencedirect.com/science/article/pii/S003132032100011X,10.1016/j.patcog.2021.107824,Live ScienceDirect subscription,live_subscription.pdf\n");
        if (springer)
            File.WriteAllText(csv,"landing_page,doi,title,save_as\nhttps://link.springer.com/article/10.1007/s11528-022-00823-9,10.1007/s11528-022-00823-9,Previously interrupted Springer PDF,live_springer_interrupted.pdf\nhttps://link.springer.com/article/10.1007/s12602-023-10045-x,10.1007/s12602-023-10045-x,Previously stalled Springer PDF,live_springer_stalled.pdf\nhttps://link.springer.com/article/10.1007/s40820-023-01226-y,10.1007/s40820-023-01226-y,Previously late Springer PDF,live_springer_late.pdf\n");
        ((TextBox)Field("txtCsv")).Text = csv;
        ((TextBox)Field("txtFolder")).Text = folder;
        ((NumericUpDown)Field("numWaitSeconds")).Value = 45;
        typeof(MainForm).GetMethod("LoadCsv", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
        await (Task)typeof(MainForm).GetMethod("StartQueueAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null)!;
        var jobs = (System.Collections.IList)Field("jobs");
        deadline = DateTime.UtcNow.AddSeconds(springer ? 240 : 115);
        while (!jobs.Cast<DownloadJob>().All(j => j.Status is JobStatus.Completed or JobStatus.Deferred or JobStatus.Failed)
            || (springer && ((System.Collections.IDictionary)Field("activeNativeDownloads")).Count > 0))
        { if (DateTime.UtcNow > deadline) break; await Task.Delay(250); }
        foreach (var job in jobs.Cast<DownloadJob>())
            Console.WriteLine(JsonSerializer.Serialize(new { job.Id, job.Doi, status = job.StatusText, job.PageDecision, job.Error, job.FilePath,
                pdf = File.Exists(job.FilePath) && File.ReadAllBytes(job.FilePath).Take(5).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("%PDF-")) }));
        Console.WriteLine("Diagnostic folder: " + typeof(MainForm).GetProperty("DiagnosticDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app));
        app.Close();
    }

    public static async Task Run(WebView2 web)
    {
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader", "WebView2ProfileV51");
        await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, profile));
        var loaded = new TaskCompletionSource();
        web.CoreWebView2.NavigationCompleted += (_, _) => loaded.TrySetResult();
        web.CoreWebView2.Navigate("https://www.sciencedirect.com/science/article/pii/S2352484724001094");
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(40));
        await Task.Delay(2500);
        var raw = await web.CoreWebView2.ExecuteScriptAsync("""
            JSON.stringify({url:location.href,title:document.title,
                controls:[...document.querySelectorAll('a,button,[role=button]')].map(el=>({tag:el.tagName,id:el.id,
                    text:(el.innerText||el.textContent||'').trim().slice(0,160),href:el.getAttribute('href'),
                    attrs:[...el.attributes].filter(a=>a.name.startsWith('data-')||['aria-label','title','target','role'].includes(a.name)).map(a=>[a.name,a.value])}))
                    .filter(el=>/pdf/i.test(el.text+' '+el.href+' '+JSON.stringify(el.attrs))).slice(0,35),
                pdfMeta:[...document.querySelectorAll('meta')].filter(el=>/pdf|pii/i.test(el.name)).map(el=>({name:el.name,content:el.content}))})
            """);
        var json = JsonSerializer.Deserialize<string>(raw)!;
        File.WriteAllText("tests/live-sciencedirect-dom.json", json);
        using var doc = JsonDocument.Parse(json);
        Console.WriteLine("Live DOM evidence saved: tests/live-sciencedirect-dom.json; controls=" + doc.RootElement.GetProperty("controls").GetArrayLength());
    }
}
