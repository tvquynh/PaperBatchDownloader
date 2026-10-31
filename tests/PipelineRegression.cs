using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class PipelineRegression
{
    public static async Task Run()
    {
        await RunCase(AccessMode.PtitIp, logout: false);
        await RunCase(AccessMode.VnuAccount, logout: true);
    }
    private static async Task RunCase(AccessMode mode, bool logout)
    {
        await using var server = new SlowPdfServer();
        string root = Path.Combine(Path.GetTempPath(), "PdfPipelineRegression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var app = new MainForm(Path.Combine(root, "profile"), mode) { ShowInTaskbar=false, Opacity=0 };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(string name) => typeof(MainForm).GetField(name, flags)!.GetValue(app)!;
        object? Call(string name, params object[] args) => typeof(MainForm).GetMethod(name, flags)!.Invoke(app, args);
        async Task Until(Func<bool> condition, int seconds=25)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception("Pipeline timeout: " + ((Label)Field("lblCurrent")).Text); await Task.Delay(50); }
        }
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        app.Show(); await Until(() => (bool)Field("initialized"));
        var core = ((WebView2)Field("web")).CoreWebView2;
        if (logout)
        {
            core.AddWebResourceRequestedFilter("https://login.openathens.net/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => e.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes("<html>Signed out.</html>")), 200, "OK", "Content-Type: text/html");
            core.CookieManager.AddOrUpdateCookie(core.CookieManager.CreateCookie("logout_wait", "present", "pipeline.fixture.invalid", "/"));
        }
        string csv = Path.Combine(root, "same-papers.csv");
        var names = logout ? new[] { "a1", "b1" } : new[] { "a1", "a2", "b1", "b2" };
        File.WriteAllText(csv, "landing_page,publisher,save_as\n" + string.Join("\n", names.Select(n=>$"{server.BaseUrl}/{n},Fixture-{n[0]},{n}.pdf")) + "\n");
        ((TextBox)Field("txtCsv")).Text = csv;
        ((TextBox)Field("txtFolder")).Text = Path.Combine(root, "pdfs");
        ((NumericUpDown)Field("numWaitSeconds")).Value = 30;
        ((NumericUpDown)Field("numTransfers")).Value = 2;
        ((NumericUpDown)Field("numPublisherTransfers")).Value = 1;
        ((NumericUpDown)Field("numPreflight")).Value = 1;
        Call("LoadCsv");
        var jobs = (IList)Field("jobs");
        await (Task)Call("StartQueueAsync")!;
        if (logout)
        {
            await Until(() => server.MaximumActive >= 2 && ((IDictionary)Field("activeNativeDownloads")).Count == 2);
            await (Task)Call("RequestVnuLogoutAsync")!;
            Check((bool)Field("logoutRequested"), "Logout did not wait for active transfers");
            Check((await core.CookieManager.GetCookiesAsync("https://pipeline.fixture.invalid/")).Count == 1, "Cookies cleared before downloads finished");
            await Until(() => !(bool)Field("logoutRequested") && !(bool)Field("logoutInProgress"));
            Check((await core.CookieManager.GetCookiesAsync("https://pipeline.fixture.invalid/")).Count == 0, "Waiting logout did not clear VNU cookie");
        }
        else await Until(() => !(bool)Field("queueRunning") && jobs.Cast<DownloadJob>().All(j => j.Status == JobStatus.Completed));
        Check(jobs.Cast<DownloadJob>().All(j => j.Status == JobStatus.Completed), "Pipeline left incomplete rows");
        Check(server.MaximumActive == 2 && server.MaximumPerPublisher <= 1, "Transfer overlap/limits were not enforced");
        foreach (var job in jobs.Cast<DownloadJob>())
            Check(File.ReadAllText(job.FilePath).Contains('/' + Path.GetFileName(job.FilePath)), "A PDF was assigned to the wrong row");
        string diagnostics = (string)typeof(MainForm).GetProperty("DiagnosticDirectory", flags)!.GetValue(app)!;
        var log = File.ReadLines(Path.Combine(diagnostics, "events.jsonl")).Select(s=>JsonDocument.Parse(s)).ToList();
        string expectedProfile = mode == AccessMode.PtitIp ? "PTIT_IP" : "VNU_ACCOUNT";
        Check(log.All(e=>e.RootElement.GetProperty("profile_id").GetString()==expectedProfile), "Log lost profile identity");
        Check(log.Any(e=>e.RootElement.GetProperty("kind").GetString()=="run-start" && e.RootElement.GetProperty("csv_sha256").GetString()!.Length==64), "Log lost CSV fingerprint/run boundary");
        Check(log.Count(e=>e.RootElement.GetProperty("kind").GetString()=="transfer-complete") == names.Length, "Completion timing records missing or duplicated");
        foreach (var record in log) record.Dispose();
        if (!logout)
        {
            Call("SaveRunSummary", false);
            using var summary = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(diagnostics,"run-summary-*.json").Single()));
            Check(summary.RootElement.GetProperty("finished").GetBoolean(), "Finished summary was overwritten by a later snapshot");
        }
        app.Close();
        Console.WriteLine($"PASS: {expectedProfile} real slow HTTP PDFs overlap at 2, per-publisher limit 1, correct file owners, run fingerprints/timings; logoutWait={logout}");
    }

    private sealed class SlowPdfServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task accepting;
        private readonly List<Task> clients = new();
        private readonly object sync = new();
        private readonly Dictionary<char,int> activePerPublisher = new();
        private int active;
        public int MaximumActive { get; private set; }
        public int MaximumPerPublisher { get; private set; }
        public string BaseUrl { get; }
        public SlowPdfServer()
        {
            listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            accepting = AcceptAsync();
        }
        private async Task AcceptAsync()
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                    lock(sync) clients.Add(ServeAsync(client));
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }
        private async Task ServeAsync(TcpClient client)
        {
            char counted = '\0';
            try
            {
                using (client)
                await using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen:true))
                {
                    string first = await reader.ReadLineAsync(cancellation.Token) ?? "";
                    string path = first.Split(' ').ElementAtOrDefault(1) ?? "/";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellation.Token))) { }
                    bool pdf = path.EndsWith(".pdf");
                    byte[] body = Encoding.UTF8.GetBytes(pdf ? "%PDF-1.4\n"+path+"\n"+new string('x',240_000)+"\n%%EOF" : $"<html><body><a href='{path}.pdf'>Download PDF</a></body></html>");
                    string header = $"HTTP/1.1 200 OK\r\nContent-Type: {(pdf ? "application/pdf" : "text/html")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n" + (pdf ? "Content-Disposition: attachment; filename=main.pdf\r\n" : "") + "\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellation.Token);
                    if (!pdf) { await stream.WriteAsync(body,cancellation.Token); return; }
                    counted = path[1];
                    lock(sync)
                    {
                        active++; activePerPublisher[counted] = activePerPublisher.GetValueOrDefault(counted)+1;
                        MaximumActive = Math.Max(MaximumActive,active);
                        MaximumPerPublisher = Math.Max(MaximumPerPublisher,activePerPublisher[counted]);
                    }
                    for (int i=0;i<body.Length;i+=16_000)
                    {
                        await stream.WriteAsync(body.AsMemory(i,Math.Min(16_000,body.Length-i)),cancellation.Token);
                        await stream.FlushAsync(cancellation.Token);
                        if (i+16_000<body.Length) await Task.Delay(150,cancellation.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally { if(counted!='\0') lock(sync) { active--; activePerPublisher[counted]--; } }
        }
        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel(); listener.Stop(); await accepting;
            Task[] pending; lock(sync) pending=clients.ToArray();
            await Task.WhenAll(pending); cancellation.Dispose();
        }
    }
}
