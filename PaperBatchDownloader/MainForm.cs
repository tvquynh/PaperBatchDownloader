using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PaperBatchDownloader;

public sealed partial class MainForm : Form
{
    private readonly TextBox txtCsv = new() { Dock = DockStyle.Fill };
    private readonly TextBox txtFolder = new() { Dock = DockStyle.Fill };
    private readonly Button btnCsv = new() { Text = "CSV...", AutoSize = true };
    private readonly Button btnFolder = new() { Text = "Folder...", AutoSize = true };
    private readonly Button btnLoad = new() { Text = "Load CSV", AutoSize = true };
    private readonly Button btnStart = new() { Text = "START", AutoSize = true };
    private readonly Button btnPause = new() { Text = "PAUSE", AutoSize = true };
    private readonly Button btnContinue = new() { Text = "CONTINUE CURRENT", AutoSize = true };
    private readonly Button btnSkip = new() { Text = "SKIP", AutoSize = true };
    private readonly Button btnExport = new() { Text = "Export result CSV", AutoSize = true };
    private readonly Button btnRetryIncomplete = new() { Text = "RETRY INCOMPLETE", AutoSize = true };
    private readonly Button btnRetryPublisher = new() { Text = "RETRY PUBLISHER", AutoSize = true };
    private readonly Button btnOpenLogin = new() { Text = "OPEN PUBLISHER LOGIN", AutoSize = true };
    private readonly NumericUpDown numPreflight = new() { Minimum = 1, Maximum = 10, Value = 3, Width = 48 };
    private readonly NumericUpDown numWaitSeconds = new() { Minimum = 15, Maximum = 600, Value = 60, Width = 62 };
    private readonly Label lblPublisherSummary = new() { AutoSize = true, Padding = new Padding(2, 2, 2, 4) };
    private readonly Label lblStats = new() { AutoSize = true, Padding = new Padding(8, 8, 8, 8) };
    private readonly Label lblCurrent = new() { AutoSize = true, Padding = new Padding(8, 4, 8, 4) };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly BindingList<DownloadJob> jobs = new();

    private int currentIndex = -1;
    private bool queueRunning;
    private bool paused;
    private bool initialized;
    private bool suppressNavigationHandling;
    private bool manualLoginMode;
    private DownloadJob? browserPdfFetchJob;
    private readonly HashSet<DownloadJob> nativePdfJobs = new();
    private readonly Dictionary<DownloadJob, CoreWebView2DownloadOperation> activeNativeDownloads = new();
    private readonly HashSet<DownloadJob> interruptedFetchFallbackTried = new();
    private readonly Dictionary<DownloadJob, string> observedPdfUrls = new();
    private readonly Dictionary<string, CoreWebView2Frame> blobFrames = new();
    private readonly Dictionary<string, DownloadJob> nativeDownloadOwners = new();
    private readonly Dictionary<ulong, DownloadJob> navigationOwners = new();
    private string downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Papers");
    private System.Windows.Forms.Timer pdfLookupTimer = new() { Interval = 2500 };
    private readonly List<int> queuePlan = new();
    private int queuePlanPosition = -1;
    private readonly System.Windows.Forms.Timer attentionTimer = new() { Interval = 1000 };
    private DateTime attentionDeadlineUtc;
    private int attentionJobId = -1;
    private bool retryMode;
    private DateTime nextPageRecheckUtc = DateTime.MinValue;
    private readonly HashSet<string> attemptedPageCandidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> rejectedPdfUrls = new(StringComparer.Ordinal);
    private readonly HashSet<string> suspendedPublishers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> downloadWatchdogFallbackTried = new();

    private sealed class SavedState
    {
        public string CsvPath { get; set; } = "";
        public List<SavedJobState> Jobs { get; set; } = new();
    }
    private sealed class SavedJobState
    {
        public int Id { get; set; }
        public string Doi { get; set; } = "";
        public JobStatus Status { get; set; }
        public string FilePath { get; set; } = "";
        public string PdfUrl { get; set; } = "";
        public string Error { get; set; } = "";
        public string DeferredReason { get; set; } = "";
        public string AttentionReason { get; set; } = "";
        public string PageDecision { get; set; } = "";
        public int Attempts { get; set; }
    }

    private sealed record NativePdfContext(string ContextUrl, string PdfUrl, DownloadJob Job);
    private NativePdfContext? nativePdfContext;
    private ulong nativePdfNavigationId;

    private readonly string? browserProfilePath;

    public MainForm(string? browserProfilePath = null)
    {
        this.browserProfilePath = browserProfilePath;
        Text = "Paper Batch Downloader V5.2.11 - VNU Library Access";
        Width = 1450;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 5, Padding = new Padding(6) };
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "CSV:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(txtCsv, 1, 0);
        top.Controls.Add(btnCsv, 2, 0);
        top.Controls.Add(btnLoad, 3, 0);
        top.Controls.Add(new Label { Text = "Download folder:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        top.Controls.Add(txtFolder, 1, 1);
        top.Controls.Add(btnFolder, 2, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new Padding(0, 4, 0, 4) };
        buttons.Controls.AddRange(new Control[] { btnStart, btnPause, btnContinue, btnSkip, btnRetryIncomplete, btnRetryPublisher, btnOpenLogin, btnExport,
            new Label { Text = "Preflight/publisher:", AutoSize = true, Padding = new Padding(10, 6, 0, 0) }, numPreflight,
            new Label { Text = "Wait timeout (s):", AutoSize = true, Padding = new Padding(10, 6, 0, 0) }, numWaitSeconds });
        top.SetColumnSpan(buttons, 4);
        top.Controls.Add(buttons, 0, 2);
        var hint = new Label
        {
            Text = "V5.2.11: Prepare VNU/OpenAthens sessions in Library access before START. Click column headers to filter; explicit paywalls defer immediately.",
            AutoSize = true,
            Padding = new Padding(2, 2, 2, 6)
        };
        top.SetColumnSpan(hint, 4);
        top.Controls.Add(hint, 0, 3);
        top.SetColumnSpan(lblPublisherSummary, 4);
        top.Controls.Add(lblPublisherSummary, 0, 4);

        var splitOuter = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 315 };
        var queueTabs = new TabControl { Dock = DockStyle.Fill };
        var queuePage = new TabPage("Download queue");
        queuePage.Controls.Add(grid);
        queueTabs.TabPages.Add(queuePage);
        InitializeLibraryAccess(queueTabs, buttons);
        splitOuter.Panel1.Controls.Add(queueTabs);
        var browserPanel = new Panel { Dock = DockStyle.Fill };
        browserPanel.Controls.Add(web);
        browserPanel.Controls.Add(lblCurrent);
        lblCurrent.Dock = DockStyle.Top;
        splitOuter.Panel2.Controls.Add(browserPanel);

        Controls.Add(splitOuter);
        Controls.Add(lblStats);
        lblStats.Dock = DockStyle.Bottom;
        Controls.Add(top);

        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Id", HeaderText = "ID", Width = 55 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Doi", HeaderText = "DOI", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Title", HeaderText = "Title", Width = 360 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PublisherKey", HeaderText = "Publisher", Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "UrlSource", HeaderText = "URL source", Width = 145 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Url", HeaderText = "Selected URL", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 260 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "StatusText", HeaderText = "Status", Width = 165 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "PageDecision", HeaderText = "Decision", Width = 210 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "FilePath", HeaderText = "File", Width = 330 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Error", HeaderText = "Error", Width = 260 });
        grid.DataSource = jobs;
        InitializeColumnFilters(queuePage);
        InitializeDiagnosticsUi(buttons);

        txtFolder.Text = downloadFolder;
        btnCsv.Click += (_, _) => ChooseCsv();
        btnFolder.Click += (_, _) => ChooseFolder();
        btnLoad.Click += (_, _) => LoadCsv();
        btnStart.Click += async (_, _) => await StartQueueAsync();
        btnPause.Click += (_, _) => PauseQueue();
        btnContinue.Click += async (_, _) => await ContinueCurrentAsync();
        btnSkip.Click += async (_, _) => await SkipCurrentAsync();
        btnExport.Click += (_, _) => ExportResults();
        btnRetryIncomplete.Click += async (_, _) => await RetryIncompleteAsync();
        btnRetryPublisher.Click += async (_, _) => await RetrySelectedPublisherAsync();
        btnOpenLogin.Click += (_, _) => OpenSelectedPublisherLogin();
        attentionTimer.Tick += async (_, _) => await AttentionTimerTickAsync();
        pdfLookupTimer.Tick += async (_, _) => await LookupPdfAsync();
        Shown += async (_, _) => await InitializeWebViewAsync();
        FormClosing += (_, _) => SaveStateSnapshot();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var profile = browserProfilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader", "WebView2ProfileV51");
            Directory.CreateDirectory(profile);

            // Keep the PDF viewer available. Disabling it creates Edge's internal
            // PDF-disabled page, whose Download button is not reliably scriptable.
            TryRestorePdfViewerPreference(profile);
            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.Profile.DefaultDownloadFolderPath = downloadFolder;
            web.CoreWebView2.Settings.AreDevToolsEnabled = true;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            web.CoreWebView2.DownloadStarting += Web_DownloadStarting;
            web.CoreWebView2.NavigationStarting += Web_NavigationStarting;
            web.CoreWebView2.FrameNavigationStarting += Web_PdfFrameNavigationStarting;
            web.CoreWebView2.NavigationCompleted += Web_NavigationCompleted;
            web.CoreWebView2.WebResourceResponseReceived += Web_WebResourceResponseReceived;
            web.CoreWebView2.AddWebResourceRequestedFilter("*://*/__paper_downloader__/*", CoreWebView2WebResourceContext.Document);
            web.CoreWebView2.WebResourceRequested += (_, e) =>
            {
                if (nativePdfContext is not { } context || e.Request.Uri != context.ContextUrl) return;
                // Supply a small local document on the PDF origin so a native download
                // link can use that origin's browser cookies and referrer.
                const string html = "<!doctype html><html><body>Saving PDF file...</body></html>";
                e.Response = web.CoreWebView2.Environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK",
                    "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
            };
            web.CoreWebView2.FrameCreated += (_, e) =>
            {
                var frame = e.Frame;
                frame.NavigationStarting += (_, args) =>
                {
                    if (!IsBlobUrl(args.Uri) || manualLoginMode || currentIndex < 0 || currentIndex >= jobs.Count) return;
                    var job = jobs[currentIndex];
                    if (!IsCurrentActiveJob(job)) return;
                    string url = args.Uri;
                    blobFrames[url] = frame;
                    args.Cancel = true;
                    BeginInvoke(new Action(async () => await StartNativePdfDownloadAsync(url, job)));
                };
            };
            await InitializePdfObservationAsync();
            InitializeDiagnosticsBrowser();
            InitializeLibraryPopupHandling();
            initialized = true;
            lblCurrent.Text = "Browser ready. Login/CAPTCHA can be completed manually here.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Cannot initialize WebView2. Install Microsoft Edge WebView2 Runtime.\n\n" + ex.Message, "WebView2 error");
        }
    }


    private void ChooseCsv()
    {
        using var d = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (d.ShowDialog() == DialogResult.OK) txtCsv.Text = d.FileName;
    }

    private static bool IsBlobUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "blob";

    private static bool IsElsevierAssetUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && new[]
        { "sciencedirectassets.com", "sciencedirect.com", "elsevier.com", "els-cdn.com" }
        .Any(domain => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private async Task ResetBrowserForJobAsync()
    {
        suppressNavigationHandling = true;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong navigationId = 0;
        void Starting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        { if (e.Uri == "about:blank") navigationId = e.NavigationId; }
        void Finished(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        { if (navigationId != 0 && e.NavigationId == navigationId) completed.TrySetResult(); }
        web.CoreWebView2.NavigationStarting += Starting;
        web.CoreWebView2.NavigationCompleted += Finished;
        try
        {
            web.CoreWebView2.Stop();
            web.CoreWebView2.Navigate("about:blank");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) { web.CoreWebView2.Stop(); }
        finally
        {
            web.CoreWebView2.NavigationStarting -= Starting;
            web.CoreWebView2.NavigationCompleted -= Finished;
            suppressNavigationHandling = false;
        }
    }

    private async Task InitializePdfObservationAsync()
    {
        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
        var owners = new Dictionary<string, (DownloadJob Job, int Attempt)>();
        web.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += (_, e) =>
        {
            if (manualLoginMode || suppressNavigationHandling || currentIndex < 0 || currentIndex >= jobs.Count) return;
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                string id = doc.RootElement.GetProperty("requestId").GetString() ?? "";
                // Redirects retain the request ID and the original row owner.
                if (!owners.ContainsKey(id)) owners[id] = (jobs[currentIndex], jobs[currentIndex].Attempts);
            }
            catch { }
        };
        web.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived").DevToolsProtocolEventReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;
                string id = root.GetProperty("requestId").GetString() ?? "";
                if (!owners.Remove(id, out var owner) || !IsCurrentActiveJob(owner.Job)
                    || owner.Attempt != owner.Job.Attempts || nativePdfJobs.Contains(owner.Job)) return;
                var response = root.GetProperty("response");
                int status = response.GetProperty("status").GetInt32();
                if (status != 200 && status != 206) return;
                string url = response.GetProperty("url").GetString() ?? "";
                string mime = response.GetProperty("mimeType").GetString() ?? "";
                string disposition = "";
                if (response.TryGetProperty("headers", out var headers))
                    foreach (var header in headers.EnumerateObject())
                        if (header.Name.Equals("content-disposition", StringComparison.OrdinalIgnoreCase)) disposition = header.Value.ToString();
                if (!IsPdfResponse(url, mime, disposition) || IsUnrelatedSpringerPdf(owner.Job, url)) return;
                observedPdfUrls[owner.Job] = url;
                if (owner.Job.Status != JobStatus.Downloading)
                {
                    owner.Job.PageDecision = "PDF network URL found";
                    RefreshRow(owner.Job);
                    pdfLookupTimer.Start();
                }
            }
            catch { }
        };
        web.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFailed").DevToolsProtocolEventReceived += (_, e) =>
        {
            try { using var doc = JsonDocument.Parse(e.ParameterObjectAsJson); owners.Remove(doc.RootElement.GetProperty("requestId").GetString() ?? ""); }
            catch { }
        };
    }

    private void ChooseFolder()
    {
        using var d = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
        if (d.ShowDialog() == DialogResult.OK) txtFolder.Text = d.SelectedPath;
    }

    private void LoadCsv()
    {
        try
        {
            if (!File.Exists(txtCsv.Text)) { MessageBox.Show("CSV file not found."); return; }
            ClearColumnFilters();
            jobs.Clear();
            foreach (var j in CsvUtils.ReadJobs(txtCsv.Text)) jobs.Add(j);
            currentIndex = -1;
            RestoreStateSnapshot();
            RefreshLibraryResources();
            UpdateStats();
            UpdatePublisherSummary();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "CSV error"); }
    }

    private async Task StartQueueAsync()
    {
        if (!initialized) { MessageBox.Show("WebView2 is not ready."); return; }
        if (jobs.Count == 0) { MessageBox.Show("Load a CSV first."); return; }
        if (!PrepareDownloadFolder()) return;

        paused = false;
        manualLoginMode = false;
        if (libraryTabs is not null) libraryTabs.SelectedIndex = 0;
        retryMode = false;
        BuildQueuePlan(j => j.Status == JobStatus.Pending);
        if (queuePlan.Count == 0)
        {
            MessageBox.Show("No pending papers. Use RETRY INCOMPLETE to retry Deferred/Failed/Skipped papers.");
            return;
        }
        queueRunning = true;
        await MoveNextAsync();
    }

    private void PauseQueue()
    {
        paused = true;
        queueRunning = false;
        pdfLookupTimer.Stop();
        attentionTimer.Stop();
        lblCurrent.Text = "Queue paused. Browser remains usable; login sessions are preserved.";
    }

    private async Task MoveNextAsync()
    {
        if (!queueRunning || paused) return;
        pdfLookupTimer.Stop();
        attentionTimer.Stop();
        attentionJobId = -1;

        int next = -1;
        while (++queuePlanPosition < queuePlan.Count)
        {
            int idx = queuePlan[queuePlanPosition];
            if (idx < 0 || idx >= jobs.Count) continue;
            if (jobs[idx].Status == JobStatus.Pending)
            {
                next = idx;
                break;
            }
        }

        if (next < 0)
        {
            queueRunning = false;
            lblCurrent.Text = "Queue pass completed. Review Deferred/Failed items or use RETRY INCOMPLETE.";
            UpdateStats();
            UpdatePublisherSummary();
            SaveStateSnapshot();
            return;
        }

        currentIndex = next;
        var job = jobs[currentIndex];
        await ResetBrowserForJobAsync();
        if (!queueRunning || paused || currentIndex != next) return;
        job.PdfUrl = "";
        observedPdfUrls.Remove(job);
        blobFrames.Clear();
        job.Attempts++;
        job.Error = "";
        job.DeferredReason = "";
        job.AttentionReason = "";
        job.PageDecision = "Navigating";
        attemptedPageCandidates.Clear();
        rejectedPdfUrls.Clear();
        interruptedFetchFallbackTried.Remove(job);
        downloadWatchdogFallbackTried.Remove(job.Id);
        nativePdfJobs.Remove(job);
        nativePdfContext = null;
        job.Status = JobStatus.Navigating;
        RefreshRow(job);
        var shortTitle = job.Title.Length > 90 ? job.Title[..90] + "…" : job.Title;
        var phase = job.IsPreflightSample ? "PREFLIGHT" : "BATCH";
        lblCurrent.Text = $"[{phase}] {queuePlanPosition + 1}/{queuePlan.Count} | {job.PublisherKey} | row {job.Id}/{jobs.Count} | {job.Doi}  {shortTitle}";
        StartAttentionDeadline(job);

        if (Uri.TryCreate(job.Url, UriKind.Absolute, out var selectedUri) &&
            selectedUri.AbsolutePath.Equals("/robots.txt", StringComparison.OrdinalIgnoreCase))
        {
            job.Status = JobStatus.Skipped;
            job.DeferredReason = "robots.txt is a site configuration file, not a paper/PDF URL";
            job.PageDecision = "Skipped: robots.txt";
            RefreshRow(job);
            SaveStateSnapshot();
            await MoveNextAsync();
            return;
        }

        // V5.2: direct OA PDF URLs should not be sent to the embedded PDF viewer first.
        // Download them with ordinary HTTP, validate %PDF-, and use WebView2 only as a
        // fallback when the publisher requires browser authentication/session state.
        if (job.IsDirectPdfCandidate && !IsWileyJob(job) && !IsIeeeDirectPdfJob(job))
        {
            job.PageDecision = "Direct HTTP PDF attempt";
            RefreshRow(job);
            if (await TryDirectHttpPdfAsync(job))
                return; // FinishDownloadAsync advances the queue.

            // A skip/timeout may have advanced the queue during the HTTP attempt.
            if (!IsCurrentActiveJob(job)) return;
            // HTTP did not yield a real PDF. Fall back to the visible browser.
            job.Status = JobStatus.Navigating;
            job.PageDecision = "Direct HTTP unavailable; browser fallback";
            job.Error = "";
            RefreshRow(job);
            StartAttentionDeadline(job);
        }

        if (IsWileyJob(job))
        {
            web.CoreWebView2.Navigate(GetWileyLandingUrl(job.Doi));
        }
        else if (IsIeeeDirectPdfJob(job) && TryGetIeeeArticleNumber(job.Url, out var ieeeArticleNumber))
        {
            job.PdfUrl = job.Url;
            web.CoreWebView2.Navigate(IsIeeeBinaryPdfUrl(job.Url) ? GetIeeeLandingUrl(ieeeArticleNumber) : job.Url);
        }
        else
        {
            web.CoreWebView2.Navigate(job.Url);
        }
    }

    private async Task<bool> TryDirectHttpPdfAsync(DownloadJob job)
    {
        string temp = "";
        try
        {
            using var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(20, (double)numWaitSeconds.Value))
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/pdf,application/octet-stream;q=0.9,*/*;q=0.8");

            job.Status = JobStatus.Downloading;
            job.PageDecision = "Direct HTTP downloading";
            RefreshRow(job);
            lblCurrent.Text = $"Direct PDF: {job.PublisherKey} | row {job.Id} | downloading...";

            using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds((double)numWaitSeconds.Value));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!IsCurrentActiveJob(job)) return false;
            if (!response.IsSuccessStatusCode)
            {
                job.PageDecision = $"Direct HTTP {(int)response.StatusCode}; browser fallback";
                return false;
            }

            string disposition = response.Content.Headers.ContentDisposition?.ToString() ?? "";
            string finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? job.Url;
            string fileName = ExtractPdfFileName(disposition, finalUrl, job.Id);
            string target = FileUtils.DownloadTargetPath(downloadFolder, job.SaveAs, fileName);
            temp = target + ".part";

            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await input.CopyToAsync(output, timeout.Token);
            }

            if (!FileUtils.HasPdfMagic(temp))
            {
                try { File.Delete(temp); } catch { }
                temp = "";
                job.PageDecision = "Direct HTTP returned non-PDF; browser fallback";
                return false;
            }

            File.Move(temp, target);
            temp = "";
            job.PdfUrl = finalUrl;
            job.FilePath = target;
            await FinishDownloadAsync(job, target);
            return true;
        }
        catch (Exception ex)
        {
            try { if (!string.IsNullOrWhiteSpace(temp) && File.Exists(temp)) File.Delete(temp); } catch { }
            job.Error = "Direct HTTP fallback: " + ex.Message;
            job.PageDecision = "Direct HTTP failed; browser fallback";
            RefreshRow(job);
            return false;
        }
    }

    private void BuildQueuePlan(Func<DownloadJob, bool> predicate)
    {
        queuePlan.Clear();
        queuePlanPosition = -1;
        foreach (var j in jobs) j.IsPreflightSample = false;

        var groups = jobs.Select((job, index) => (job, index))
            .Where(x => predicate(x.job))
            .GroupBy(x => string.IsNullOrWhiteSpace(x.job.PublisherKey) ? "Other" : x.job.PublisherKey)
            .ToList();

        int preflightCount = (int)numPreflight.Value;
        var remaining = new List<Queue<int>>();
        foreach (var group in groups)
        {
            var indices = group.Select(x => x.index).ToList();
            foreach (var idx in indices.Take(preflightCount))
            {
                jobs[idx].IsPreflightSample = true;
                queuePlan.Add(idx);
            }
            remaining.Add(new Queue<int>(indices.Skip(preflightCount)));
        }

        bool more;
        do
        {
            more = false;
            foreach (var q in remaining)
            {
                if (q.Count == 0) continue;
                queuePlan.Add(q.Dequeue());
                more = true;
            }
        } while (more);

        UpdatePublisherSummary();
    }

    private void BuildPublisherRetryPlan(string publisher)
    {
        BuildQueuePlan(j => j.Status == JobStatus.Pending && j.PublisherKey.Equals(publisher, StringComparison.OrdinalIgnoreCase));
    }

    private void StartAttentionDeadline(DownloadJob job)
    {
        attentionJobId = job.Id;
        attentionDeadlineUtc = DateTime.UtcNow.AddSeconds((double)numWaitSeconds.Value);
        nextPageRecheckUtc = DateTime.UtcNow;
        attentionTimer.Stop();
        attentionTimer.Start();
    }

    private void CancelAttentionDeadline(DownloadJob job)
    {
        if (attentionJobId != job.Id) return;
        attentionTimer.Stop();
        attentionJobId = -1;
    }

    private static bool NeedsUserAction(JobStatus status)
        => status is JobStatus.WaitingForUser or JobStatus.LoginRequired or JobStatus.BotCheck
            or JobStatus.InstitutionalAccessRequired or JobStatus.ManualAttention;

    private sealed class PageDecisionResult
    {
        public string Classification { get; set; } = "Unknown";
        public string CurrentUrl { get; set; } = "";
        public string Title { get; set; } = "";
        public string Evidence { get; set; } = "";
        public List<string> PdfCandidates { get; set; } = new();
        public string PageText { get; set; } = "";
        public List<string> Frames { get; set; } = new();
        public string ContentType { get; set; } = "";
    }

    private async Task<PageDecisionResult> AnalyzeCurrentPageAsync()
    {
        var result = new PageDecisionResult();
        try
        {
            string js = """
            (() => {
                const txt = ((document.body && (document.body.innerText || document.body.textContent)) || '').replace(/\s+/g,' ').slice(0,120000);
                const low = txt.toLowerCase();
                const title = document.title || '';
                const url = location.href || '';
                const scienceDirect = /(^|\.)sciencedirect\.com$/i.test(location.hostname);
                const articlePii = scienceDirect && (location.pathname.match(/\/pii\/([A-Z0-9]+)/i)||[])[1];
                const springer = /(^|\.)(springer\.com|springernature\.com)$/i.test(location.hostname);
                const springerDoi = springer && ((document.querySelector('meta[name="citation_doi"]')||{}).content
                    || (location.pathname.match(/\/article\/(10\.\d{4,9}\/.+)/i)||[])[1]);
                const candidates = [];
                const add = (u, score, why, allowCurrent = false) => {
                    if (!u) return;
                    try {
                        const parsed = new URL(u, location.href);
                        if (!['http:', 'https:', 'blob:'].includes(parsed.protocol)) return;
                        if (parsed.href === location.href && !allowCurrent) return;
                        // Reference lists contain many unrelated View PDF links.
                        // On an article page only accept PDFs for its own PII.
                        if (articlePii && !parsed.href.toUpperCase().includes(articlePii.toUpperCase())) return;
                        if (springerDoi && parsed.protocol !== 'blob:'
                            && !decodeURIComponent(parsed.href).toLowerCase().includes(decodeURIComponent(springerDoi).toLowerCase())) return;
                        if (springerDoi && /\/esm\/|\/MediaObjects\/|MOESM|_ESM\.pdf/i.test(decodeURIComponent(parsed.pathname))) return;
                        if (/(^|\.)(disqus\.com|doubleclick\.net|googlesyndication\.com)$/.test(parsed.hostname)) return;
                        candidates.push({u:parsed.href,score,why});
                    } catch(e) {}
                };
                document.querySelectorAll('embed[original-url], [data-original-url]').forEach(el =>
                    add(el.getAttribute('original-url') || el.getAttribute('data-original-url'), 200, 'PDF viewer original URL', true));
                if ((document.contentType || '').toLowerCase().includes('pdf')) add(location.href, 210, 'PDF document', true);
                document.querySelectorAll('iframe[src^="blob:"],embed[src^="blob:"],object[data^="blob:"]').forEach(el =>
                    add(el.src || el.data, 190, 'browser PDF blob', true));
                const looksPdf = u => /\.pdf(?:[?#]|$)|\/(?:pdf|pdfdirect|pdfft)\/|\/stamp\/stamp(?:pdf)?\.jsp/i.test(u || '');
                document.querySelectorAll('meta[name="citation_pdf_url"], meta[name="wkhealth_pdf_url"], meta[name="eprints.document_url"]')
                    .forEach(m => add(m.content, 140, 'meta citation_pdf_url'));
                document.querySelectorAll('iframe[src], embed[src], object[data]').forEach(el => {
                    const u = el.src || el.data;
                    if (looksPdf(u) || (el.type || '').toLowerCase() === 'application/pdf') add(u, 90, 'embedded PDF');
                });
                // IEEE exposes its PDF through stamp.jsp, including article pages
                // where the PDF button is implemented by site JavaScript.
                const ieeeArticle = location.hostname === 'ieeexplore.ieee.org'
                    && location.pathname.match(/^\/document\/(\d+)\/?$/);
                if (ieeeArticle) add('/stamp/stamp.jsp?tp=&arnumber=' + ieeeArticle[1], 150, 'IEEE article PDF');
                [...document.querySelectorAll('a[href]')].forEach(a => {
                    const h=(a.href||'').toLowerCase(), t=((a.innerText||a.textContent||'')+'').replace(/\s+/g,' ').trim().toLowerCase(); let score=0;
                    if (t.includes('download pdf')) score+=130;
                    if (t==='pdf' || t.includes('view pdf') || t.includes('full text pdf') || t.includes('pdf full text')) score+=100;
                    if (h.endsWith('.pdf')) score+=115;
                    if (h.includes('.pdf?') || h.includes('/pdf/') || h.includes('/pdfdirect/') || h.includes('/pdfft') || h.includes('pdfdownload')) score+=95;
                    if (h.includes('download') && t.includes('pdf')) score+=80;
                    if (h.includes('/stamp/stamp.jsp') || h.includes('/stamp/stamppdf.jsp')) score+=120;
                    if (score>0) add(a.href, score, t.slice(0,100));
                });
                const uniq = [...new Map(candidates.sort((a,b)=>b.score-a.score).map(x=>[x.u,x])).values()].slice(0,8);
                const has=(...xs)=>xs.some(x=>low.includes(x));
                const fullAccess = has('you have full access to this','you have access to this article');
                let cls='Normal', evidence='';
                if (has('verify you are human','checking your browser','captcha','recaptcha','hcaptcha','cloudflare','are you a robot','unusual traffic','security check')) { cls='BotCheck'; evidence='bot/CAPTCHA challenge text'; }
                else if (!fullAccess && has('does not subscribe to this content','does not have access to this content','your institution does not have access')) { cls='EntitlementDenied'; evidence='institution does not subscribe to this article'; }
                else if (!fullAccess && has('purchase access','buy this article','buy article pdf','subscribe to access','subscription required','this is a preview of subscription content')) { cls='Paywall'; evidence='article purchase/subscription required'; }
                else if (has('please sign in through your institution','institutional login required','please select your institution','openathens login','shibboleth login')) { cls='InstitutionalAccessRequired'; evidence='institutional access text'; }
                else if ((has('please sign in','please log in','login required','sign in to access','authentication required') || document.querySelector('input[type=password]')) && !has('signed in as','logout','log out')) { cls='LoginRequired'; evidence='login/sign-in text'; }
                else if (!fullAccess && has('access denied','you do not have access','not entitled')) { cls='AccessRequired'; evidence='access/authentication text'; }
                else if (has('page not found','article not found','404 not found','the requested page could not be found')) { cls='NotFound'; evidence='not found text'; }
                else if (has('pdf reader has been disabled','always download pdf files')) { cls='PdfInterstitial'; evidence='Edge/WebView2 PDF interstitial'; }
                else if (uniq.length>0) { cls='PdfCandidateFound'; evidence=uniq[0].why || 'PDF link'; }
                return JSON.stringify({classification:cls,currentUrl:url,title,evidence,pdfCandidates:uniq.map(x=>x.u),
                    pageText:txt.slice(0,24000),contentType:document.contentType,
                    frames:[...document.querySelectorAll('iframe[src],embed[src],object[data]')].slice(0,30).map(x=>x.src||x.data)});
            })();
            """;
            string raw = await web.CoreWebView2.ExecuteScriptAsync(js).WaitAsync(TimeSpan.FromSeconds(10));
            string json = JsonSerializer.Deserialize<string>(raw) ?? "";
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                result.Classification = root.TryGetProperty("classification", out var c) ? c.GetString() ?? "Unknown" : "Unknown";
                result.CurrentUrl = root.TryGetProperty("currentUrl", out var u) ? u.GetString() ?? "" : "";
                result.Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                result.Evidence = root.TryGetProperty("evidence", out var ev) ? ev.GetString() ?? "" : "";
                result.PageText = root.TryGetProperty("pageText", out var pt) ? pt.GetString() ?? "" : "";
                result.ContentType = root.TryGetProperty("contentType", out var ct) ? ct.GetString() ?? "" : "";
                if (root.TryGetProperty("frames", out var frames))
                    result.Frames = frames.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (root.TryGetProperty("pdfCandidates", out var a) && a.ValueKind == JsonValueKind.Array)
                    result.PdfCandidates = a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            }
        }
        catch (Exception ex)
        {
            result.Classification = "AnalysisError";
            result.Evidence = ex.Message;
        }
        return result;
    }

    private async Task<bool> ApplyPageDecisionAsync(DownloadJob job, bool fromWaitingRecheck = false)
    {
        if (manualLoginMode) return true;
        if (nativePdfContext?.Job == job) return true;
        if (browserPdfFetchJob is not null || web.CoreWebView2 is null || job.Status is JobStatus.Completed or JobStatus.Downloading) return false;
        if (observedPdfUrls.TryGetValue(job, out var observed) && !nativePdfJobs.Contains(job))
        {
            if (await StartNativePdfDownloadAsync(observed, job, requireContext: !IsBlobUrl(observed))) return true;
        }
        var d = await AnalyzeCurrentPageAsync();
        LogDiagnostic("page-analysis", job, d);
        if (manualLoginMode || !IsCurrentActiveJob(job)) return true;
        job.PageDecision = string.IsNullOrWhiteSpace(d.Evidence) ? d.Classification : $"{d.Classification}: {d.Evidence}";
        RefreshRow(job);

        if (await TryDeferPaywallAsync(job, d)) return true;

        // First try legitimate PDF links exposed by the current authenticated page.
        foreach (var candidate in d.PdfCandidates.Take(4))
        {
            if (!attemptedPageCandidates.Add(candidate)) continue;
            job.PdfUrl = candidate;
            job.PageDecision = $"Trying PDF candidate: {candidate}";
            RefreshRow(job);
            lblCurrent.Text = $"{job.PublisherKey}: PDF candidate found by page analysis; trying authenticated download...";
            if (await TryTriggerPdfDownloadInPageAsync(candidate, job))
            {
                return true; // A browser navigation/download is now pending.
            }
        }

        switch (d.Classification)
        {
            case "BotCheck":
                SetWaitingForUser(job, "Bot/CAPTCHA challenge detected. Complete it in the visible browser; the app will re-check automatically.", null, JobStatus.BotCheck, "Bot/CAPTCHA challenge", !fromWaitingRecheck);
                return true;
            case "InstitutionalAccessRequired":
                SetWaitingForUser(job, "Institutional access/login detected. Sign in in the visible browser; the app will re-check automatically.", null, JobStatus.InstitutionalAccessRequired, "Institutional access/login", !fromWaitingRecheck);
                return true;
            case "LoginRequired":
                SetWaitingForUser(job, "Login required. Sign in in the visible browser; the app will re-check automatically.", null, JobStatus.LoginRequired, "Publisher login required", !fromWaitingRecheck);
                return true;
            case "AccessRequired":
                SetWaitingForUser(job, "Publisher reports that access/authentication may be required. Resolve access in the visible browser; the app will re-check automatically.", null, JobStatus.ManualAttention, "Access/paywall/authentication required", !fromWaitingRecheck);
                return true;
            case "NotFound":
                job.Status = JobStatus.Failed;
                job.Error = "Page analysis: article/page not found.";
                job.DeferredReason = "Permanent page-not-found response";
                RefreshRow(job); UpdateStats(); SaveStateSnapshot();
                lblCurrent.Text = $"Row {job.Id}: page not found; continuing.";
                if (!paused) { queueRunning = true; await MoveNextAsync(); }
                return true;
            case "PdfInterstitial":
                // Network/CDP capture and publisher-specific handlers still have a chance.
                job.PageDecision = "PDF interstitial detected; waiting for network/provider handler";
                RefreshRow(job);
                return false;
            default:
                return false;
        }
    }

    private async Task<bool> TryDeferPaywallAsync(DownloadJob job, PageDecisionResult decision)
    {
        if (decision.Classification is not ("Paywall" or "EntitlementDenied")) return false;
        if (manualLoginMode || !IsCurrentActiveJob(job) || job.Status == JobStatus.Downloading
            || (activeNativeDownloads.TryGetValue(job, out var download) && download.State == CoreWebView2DownloadState.InProgress)) return true;
        pdfLookupTimer.Stop();
        CancelAttentionDeadline(job);
        job.Status = JobStatus.Deferred;
        job.DeferredReason = "Paywall";
        job.AttentionReason = "";
        job.Error = decision.Classification == "EntitlementDenied"
            ? "Publisher explicitly reports no institutional subscription for this article."
            : "Publisher requires purchase/subscription for this article.";
        job.PageDecision = "Deferred - Paywall (no timeout wait)";
        RefreshRow(job); UpdateStats(); UpdatePublisherSummary(); SaveStateSnapshot();
        lblCurrent.Text = $"Row {job.Id}: paywall detected; continuing to the next article.";
        if (queueRunning && !paused) await MoveNextAsync();
        return true;
    }

    private async Task AttentionTimerTickAsync()
    {
        if (currentIndex < 0 || currentIndex >= jobs.Count) { attentionTimer.Stop(); return; }
        var job = jobs[currentIndex];
        if (attentionJobId != job.Id) { attentionTimer.Stop(); return; }

        if (job.Status == JobStatus.Completed)
        {
            CancelAttentionDeadline(job);
            return;
        }

        // The host also bounds Runtime.evaluate, independently of page timers.
        if (browserPdfFetchJob == job)
        {
            lblCurrent.Text = $"Waiting for PDF request; {Math.Max(0, Math.Ceiling((attentionDeadlineUtc - DateTime.UtcNow).TotalSeconds))}s remaining...";
            return;
        }

        var remaining = attentionDeadlineUtc - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            if (job.Status == JobStatus.Downloading)
            {
                lblCurrent.Text = $"{job.PublisherKey}: waiting for PDF file download; watchdog {Math.Ceiling(remaining.TotalSeconds)}s.";
                return;
            }
            if (NeedsUserAction(job.Status))
            {
                if (DateTime.UtcNow >= nextPageRecheckUtc)
                {
                    nextPageRecheckUtc = DateTime.UtcNow.AddSeconds(5);
                    var before = job.Status;
                    bool handled = await ApplyPageDecisionAsync(job, true);
                    if (job.Status is JobStatus.Downloading or JobStatus.Completed || (handled && !NeedsUserAction(job.Status))) return;
                    // If the challenge/login text disappeared, retry PDF discovery automatically.
                    if (NeedsUserAction(before) && !NeedsUserAction(job.Status)) return;
                    var page = await AnalyzeCurrentPageAsync();
                    if (page.Classification is "Normal" or "PdfCandidateFound" or "PdfInterstitial")
                    {
                        job.Status = JobStatus.LookingForPdf;
                        job.AttentionReason = "";
                        job.Error = "";
                        RefreshRow(job);
                        await LookupPdfAsync();
                        return;
                    }
                }
                lblCurrent.Text = $"{job.PublisherKey}: {job.AttentionReason}. Auto-defer in {Math.Ceiling(remaining.TotalSeconds)}s; browser page is re-checked automatically.";
            }
            return;
        }

        if (job.Status == JobStatus.Downloading)
        {
            if (activeNativeDownloads.TryGetValue(job, out var active) && active.State == CoreWebView2DownloadState.InProgress)
            {
                attentionTimer.Stop();
                attentionJobId = -1;
                job.Status = JobStatus.Deferred;
                job.DeferredReason = "Native download idle; continuing in background";
                job.Error = $"No download progress for {(int)numWaitSeconds.Value}s. The browser may still finish this file in the background.";
                job.PageDecision = "Waiting for background download completion";
                RefreshRow(job); SaveStateSnapshot();
                if (!paused) { queueRunning = true; await MoveNextAsync(); }
                return;
            }
            // A PDF response/download was detected but never completed. Try one browser-session
            // fallback using the captured PDF URL, then defer instead of blocking the queue forever.
            if (!downloadWatchdogFallbackTried.Contains(job.Id))
            {
                downloadWatchdogFallbackTried.Add(job.Id);
                string fallbackUrl = job.PdfUrl;
                job.Status = JobStatus.LookingForPdf;
                job.PageDecision = "Download watchdog timeout; trying one authenticated fallback";
                job.Error = $"No completed PDF after {(int)numWaitSeconds.Value}s; fallback retry started.";
                RefreshRow(job);

                bool started = false;
                if (!string.IsNullOrWhiteSpace(fallbackUrl) &&
                    (fallbackUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || fallbackUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    nativePdfJobs.Remove(job);
                    started = await TryTriggerPdfDownloadInPageAsync(fallbackUrl, job);
                }

                if (started)
                {
                    job.Status = JobStatus.Downloading;
                    job.PageDecision = "Fallback download triggered; waiting for completion";
                    RefreshRow(job);
                    StartAttentionDeadline(job);
                    lblCurrent.Text = $"{job.PublisherKey}: fallback download triggered; waiting up to {(int)numWaitSeconds.Value}s for completion.";
                    return;
                }
            }

            attentionTimer.Stop();
            attentionJobId = -1;
            job.Status = JobStatus.Deferred;
            job.DeferredReason = "File download timeout";
            job.Error = $"PDF was detected but no complete file was saved after watchdog/fallback ({(int)numWaitSeconds.Value}s).";
            job.PageDecision = "Deferred: download watchdog timeout";
            RefreshRow(job);
            UpdateStats();
            SaveStateSnapshot();
            lblCurrent.Text = $"Deferred row {job.Id}: PDF capture/download timed out; continuing queue.";
            if (!paused)
            {
                queueRunning = true;
                await MoveNextAsync();
            }
            return;
        }

        attentionTimer.Stop();
        attentionJobId = -1;
        if (job.Status != JobStatus.Completed)
        {
            bool publisherNeedsUser = job.Status is JobStatus.LoginRequired or JobStatus.BotCheck or JobStatus.InstitutionalAccessRequired;
            job.Status = JobStatus.Deferred;
            job.DeferredReason = publisherNeedsUser ? (string.IsNullOrWhiteSpace(job.AttentionReason) ? "User-action timeout" : job.AttentionReason + " timeout") : "Navigation/lookup timeout";
            if (string.IsNullOrWhiteSpace(job.Error)) job.Error = $"Deferred after {(int)numWaitSeconds.Value}s without successful PDF completion.";
            RefreshRow(job);

            if (publisherNeedsUser)
            {
                suspendedPublishers.Add(job.PublisherKey);
                // Once a publisher clearly needs user assistance, do not spend the rest of
                // the unattended run repeatedly waiting on the same login/CAPTCHA state.
                foreach (var other in jobs.Where(j => j.PublisherKey.Equals(job.PublisherKey, StringComparison.OrdinalIgnoreCase) && j.Status == JobStatus.Pending))
                {
                    other.Status = JobStatus.Deferred;
                    other.DeferredReason = "Publisher suspended after login/CAPTCHA timeout";
                    other.Error = $"{job.PublisherKey} suspended for this pass. Login manually, then use RETRY PUBLISHER.";
                    RefreshRow(other);
                }
            }

            SaveStateSnapshot();
            lblCurrent.Text = publisherNeedsUser
                ? $"{job.PublisherKey} suspended after user timeout; continuing other publishers. Login later and use RETRY PUBLISHER."
                : $"Deferred row {job.Id} ({job.PublisherKey}); continuing with the next publisher/job.";
            if (!paused)
            {
                queueRunning = true;
                await MoveNextAsync();
            }
        }
    }

    private void SetWaitingForUser(DownloadJob job, string message, string? error = null, JobStatus attentionStatus = JobStatus.WaitingForUser, string? attentionReason = null, bool resetDeadline = true)
    {
        if (manualLoginMode || !IsCurrentActiveJob(job)) return;
        job.Status = attentionStatus;
        job.AttentionReason = attentionReason ?? attentionStatus.ToString();
        job.PageDecision = job.AttentionReason;
        if (!string.IsNullOrWhiteSpace(error)) job.Error = error;
        RefreshRow(job);
        UpdateStats();
        UpdatePublisherSummary();
        lblCurrent.Text = message;
        // Keep queue logically active, but remain on this browser page for the configured
        // user-assistance window. AttentionTimerTickAsync will defer and continue automatically.
        queueRunning = true;
        // Re-analysis of the same unchanged page must not restart the timeout.
        if (resetDeadline && attentionJobId != job.Id) StartAttentionDeadline(job);
        SaveStateSnapshot();
    }

    private async Task RetryIncompleteAsync()
    {
        if (jobs.Count == 0) return;
        if (!PrepareDownloadFolder()) return;
        foreach (var job in jobs)
        {
            if (activeNativeDownloads.TryGetValue(job, out var active) && active.State == CoreWebView2DownloadState.InProgress) continue;
            if (job.Status is JobStatus.Deferred or JobStatus.Failed or JobStatus.Skipped || NeedsUserAction(job.Status))
            {
                if (!PreserveUntrustedSavedPdf(job)) continue;
                job.Status = JobStatus.Pending;
                job.Error = "";
                job.DeferredReason = "";
                RefreshRow(job);
            }
        }
        retryMode = true;
        manualLoginMode = false;
        suspendedPublishers.Clear();
        paused = false;
        queueRunning = true;
        BuildQueuePlan(j => j.Status == JobStatus.Pending);
        await MoveNextAsync();
    }

    private async Task RetrySelectedPublisherAsync()
    {
        if (jobs.Count == 0) return;
        if (!PrepareDownloadFolder()) return;
        var selected = grid.CurrentRow?.DataBoundItem as DownloadJob;
        if (selected is null && currentIndex >= 0 && currentIndex < jobs.Count) selected = jobs[currentIndex];
        if (selected is null) { MessageBox.Show("Select a row/publisher first."); return; }
        string publisher = selected.PublisherKey;
        foreach (var job in jobs.Where(j => j.PublisherKey.Equals(publisher, StringComparison.OrdinalIgnoreCase)))
        {
            if (activeNativeDownloads.TryGetValue(job, out var active) && active.State == CoreWebView2DownloadState.InProgress) continue;
            if (job.Status is JobStatus.Deferred or JobStatus.Failed or JobStatus.Skipped || NeedsUserAction(job.Status))
            {
                if (!PreserveUntrustedSavedPdf(job)) continue;
                job.Status = JobStatus.Pending;
                job.Error = "";
                job.DeferredReason = "";
                RefreshRow(job);
            }
        }
        retryMode = true;
        manualLoginMode = false;
        suspendedPublishers.Remove(publisher);
        paused = false;
        queueRunning = true;
        BuildPublisherRetryPlan(publisher);
        if (queuePlan.Count == 0) { MessageBox.Show($"No incomplete jobs for {publisher}."); return; }
        await MoveNextAsync();
    }

    private void OpenSelectedPublisherLogin()
    {
        if (!initialized) return;
        var selected = grid.CurrentRow?.DataBoundItem as DownloadJob;
        if (selected is null && currentIndex >= 0 && currentIndex < jobs.Count) selected = jobs[currentIndex];
        if (selected is null) { MessageBox.Show("Select a publisher row first."); return; }
        RefreshLibraryResources();
        var resource = libraryResources.FirstOrDefault(r => MatchesLibraryResource(r, selected));
        if (resource is not null) { OpenLibraryResource(resource); return; }
        PauseQueue();
        manualLoginMode = true;
        string login = PublisherResolver.LoginUrl(selected.PublisherKey);
        if (string.IsNullOrWhiteSpace(login)) login = selected.Url;
        web.CoreWebView2.Navigate(login);
        lblCurrent.Text = $"Manual login mode: {selected.PublisherKey}. Sign in here; the WebView2 V5.1 profile will preserve the session for later runs.";
    }

    private void Web_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (manualLoginMode || suppressNavigationHandling || currentIndex < 0 || currentIndex >= jobs.Count) return;

        navigationOwners[e.NavigationId] = jobs[currentIndex];
        if (nativePdfContext is { } pendingContext && e.Uri == pendingContext.ContextUrl)
            nativePdfNavigationId = e.NavigationId;
        if (IsBlobUrl(e.Uri))
        {
            var blobJob = jobs[currentIndex];
            if (IsCurrentActiveJob(blobJob) && !nativePdfJobs.Contains(blobJob))
            {
                string blobUrl = e.Uri;
                e.Cancel = true;
                BeginInvoke(new Action(async () => await StartNativePdfDownloadAsync(blobUrl, blobJob)));
            }
            return;
        }
        if (IsBinaryPdfUrl(e.Uri) && !IsScienceDirectWrapperUrl(e.Uri) && !rejectedPdfUrls.Contains(e.Uri))
        {
            var job = jobs[currentIndex];
            if (!IsCurrentActiveJob(job) || nativePdfJobs.Contains(job)) return;
            string url = e.Uri;
            e.Cancel = true;
            BeginInvoke(new Action(async () => await StartNativePdfDownloadAsync(url, job)));
            return;
        }
        // With the PDF viewer enabled, let IEEE stamp pages and PDF redirects
        // load normally. Cancelling them to reopen the article creates a loop.
        if (IsIeeePdfUrl(e.Uri))
        {
            var job = jobs[currentIndex];
            if (job.Status is JobStatus.Completed or JobStatus.Skipped) return;
            job.PdfUrl = e.Uri;
            if (IsIeeeBinaryPdfUrl(e.Uri))
            {
                string pdfUrl = e.Uri;
                e.Cancel = true;
                BeginInvoke(new Action(async () => await StartNativePdfDownloadAsync(pdfUrl, job)));
                return;
            }
            job.PageDecision = "Opening IEEE PDF in browser";
            RefreshRow(job);
        }
    }

    private void Web_PdfFrameNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (manualLoginMode || currentIndex < 0 || currentIndex >= jobs.Count || !IsBinaryPdfUrl(e.Uri) || IsScienceDirectWrapperUrl(e.Uri)) return;
        var job = jobs[currentIndex];
        if (job.Status is JobStatus.Completed or JobStatus.Skipped) return;
        // Download the iframe's real PDF as a browser-managed file instead of handing
        // it to the PDF viewer, whose stream/CDP response body may be unavailable.
        string url = e.Uri;
        e.Cancel = true;
        BeginInvoke(new Action(async () => await StartNativePdfDownloadAsync(url, job)));
    }

    private async Task<bool> StartNativePdfDownloadAsync(string url, DownloadJob job, bool requireContext = false)
    {
        if (manualLoginMode || !IsCurrentActiveJob(job)) return false;
        if (IsUnrelatedSpringerPdf(job, url)) return false;
        if (rejectedPdfUrls.Contains(url)) return false;
        if (nativePdfContext?.Job == job || nativePdfJobs.Contains(job)) return true;
        job.PdfUrl = url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            (target.Scheme != "https" && target.Scheme != "http" && target.Scheme != "blob")) return false;
        if (nativePdfJobs.Contains(job)) return true;
        if (target.Scheme != "blob" && (requireContext || !Uri.TryCreate(web.CoreWebView2.Source, UriKind.Absolute, out var source) ||
            IsBinaryPdfUrl(source.AbsoluteUri) ||
            source.GetLeftPart(UriPartial.Authority) != target.GetLeftPart(UriPartial.Authority)))
        {
            string contextUrl = target.GetLeftPart(UriPartial.Authority) + "/__paper_downloader__/" + Guid.NewGuid().ToString("N");
            nativePdfContext = new NativePdfContext(contextUrl, url, job);
            nativePdfNavigationId = 0;
            job.Status = JobStatus.Navigating;
            job.PageDecision = "Preparing browser file download";
            RefreshRow(job);
            StartAttentionDeadline(job);
            web.CoreWebView2.Navigate(contextUrl);
            return true;
        }
        if (!nativePdfJobs.Add(job)) return true;
        try
        {
            job.Status = JobStatus.Downloading;
            job.PageDecision = "Browser file download";
            RefreshRow(job);
            StartAttentionDeadline(job);
            string script = $$"""
                (() => {
                    const a = document.createElement('a');
                    a.href = {{JsonSerializer.Serialize(url)}};
                    a.download = {{JsonSerializer.Serialize(string.IsNullOrWhiteSpace(job.SaveAs) ? $"paper_{job.Id:D5}.pdf" : job.SaveAs)}};
                    document.body.appendChild(a);
                    a.click();
                    a.remove();
                    return true;
                })()
                """;
            nativeDownloadOwners[url] = job;
            if (IsBlobUrl(url) && blobFrames.TryGetValue(url, out var frame))
                await frame.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(10));
            else
                await web.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(10));
            return true;
        }
        catch (Exception ex)
        {
            nativePdfJobs.Remove(job);
            if (!IsCurrentActiveJob(job)) return false;
            job.Status = JobStatus.LookingForPdf;
            job.Error = "Browser file download could not start: " + ex.Message;
            RefreshRow(job);
            return false;
        }
    }

    private async void Web_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (manualLoginMode || suppressNavigationHandling || currentIndex < 0 || currentIndex >= jobs.Count) return;
        var job = jobs[currentIndex];
        if (navigationOwners.TryGetValue(e.NavigationId, out var navigationJob) && navigationJob != job) return;
        if (!queueRunning && !NeedsUserAction(job.Status)) return;
        if (job.Status is JobStatus.Completed or JobStatus.Downloading) return;
        if (nativePdfContext is { } pending)
        {
            // Completions from the page being replaced must not restart setup.
            if (e.NavigationId != nativePdfNavigationId) return;
            nativePdfContext = null;
            if (!e.IsSuccess)
            {
                observedPdfUrls.Remove(job);
                SetWaitingForUser(job, "PDF download setup needs attention.", "Navigation error: " + e.WebErrorStatus);
                return;
            }
            if (pending.Job == job && IsCurrentActiveJob(job)) await StartNativePdfDownloadAsync(pending.PdfUrl, job);
            return;
        }
        if (nativePdfContext is null && observedPdfUrls.TryGetValue(job, out var observedPdf) && !nativePdfJobs.Contains(job))
        {
            if (await StartNativePdfDownloadAsync(observedPdf, job, requireContext: !IsBlobUrl(observedPdf))) return;
        }
        if (NeedsUserAction(job.Status) && nativePdfJobs.Contains(job)) return;
        if (!e.IsSuccess && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;

        if (!e.IsSuccess)
        {
            if (await ApplyPageDecisionAsync(job)) return;
            SetWaitingForUser(job,
                "Navigation failed. The visible page will be re-checked automatically; you may fix login/access manually.",
                "Navigation error: " + e.WebErrorStatus, JobStatus.ManualAttention, "Navigation error");
            return;
        }

        job.Status = JobStatus.LookingForPdf;
        RefreshRow(job);

        if (IsBinaryPdfUrl(job.PdfUrl) && !IsScienceDirectWrapperUrl(job.PdfUrl) && !rejectedPdfUrls.Contains(job.PdfUrl) && await StartNativePdfDownloadAsync(job.PdfUrl, job)) return;

        // Detect explicit purchase/subscription pages as soon as navigation completes.
        var pageDecision = await AnalyzeCurrentPageAsync();
        if (manualLoginMode || !IsCurrentActiveJob(job) || job.Status == JobStatus.Downloading) return;
        if (pageDecision.Classification is "Paywall" or "EntitlementDenied")
        {
            LogDiagnostic("page-analysis", job, pageDecision);
            if (await TryDeferPaywallAsync(job, pageDecision)) return;
        }

        // Wiley special case: once the normal article landing page is loaded, request
        // the documented/observed pdfdirect endpoint from the same Wiley origin.
        // This reuses WebView2 cookies/session and produces DownloadStarting via Blob,
        // avoiding Edge's non-scriptable internal PDF-disabled interstitial.
        if (IsWileyJob(job))
        {
            string wileyPdf = GetWileyPdfDirectUrl(job.Doi);
            job.PdfUrl = wileyPdf;
            RefreshRow(job);
            lblCurrent.Text = "Wiley article loaded; requesting pdfdirect with the current browser session...";
            bool triggered = await TryTriggerPdfDownloadInPageAsync(wileyPdf, job);
            if (triggered)
            {
                await Task.Delay(1800);
                if (job.Status == JobStatus.Downloading || job.Status == JobStatus.Completed) return;
            }

            // If the user still needs institutional login/CAPTCHA, leave the visible
            // Wiley landing page open instead of navigating into the PDF interstitial.
            SetWaitingForUser(job,
                "Wiley needs browser attention. Complete login/CAPTCHA if shown, then click CONTINUE CURRENT; otherwise it will auto-defer.",
                string.IsNullOrWhiteSpace(job.Error) ? "Wiley pdfdirect did not start. Login/CAPTCHA may be required." : job.Error);
            return;
        }

        // When Chromium's built-in PDF reader is disabled, WebView2 can show an
        // internal interstitial that says "PDF reader has been disabled in Microsoft Edge"
        // and exposes a "Download file" button. Click that button automatically so the
        // normal DownloadStarting event handles the file without user intervention.
        if (await TryClickDisabledPdfDownloadPageAsync(job)) return;

        pdfLookupTimer.Stop();
        pdfLookupTimer.Start();
    }

    private async Task LookupPdfAsync()
    {
        pdfLookupTimer.Stop();
        if (manualLoginMode) return;
        if (currentIndex < 0 || currentIndex >= jobs.Count || web.CoreWebView2 is null) return;
        var job = jobs[currentIndex];
        if (browserPdfFetchJob is not null || job.Status is JobStatus.Completed or JobStatus.Downloading or JobStatus.Skipped) return;

        try
        {
            // Re-check the Edge/WebView2 PDF-disabled interstitial. Some publishers
            // redirect to it only after the first NavigationCompleted event.
            if (await TryClickDisabledPdfDownloadPageAsync(job)) return;

            // V5.1 smart decision layer: inspect the visible authenticated page first.
            // It may find citation_pdf_url/iframe/PDF links, or classify login/bot/access states.
            if (await ApplyPageDecisionAsync(job)) return;

            if (Uri.TryCreate(web.CoreWebView2.Source, UriKind.Absolute, out var articleSource)
                && (articleSource.Host.Equals("sciencedirect.com", StringComparison.OrdinalIgnoreCase)
                    || articleSource.Host.EndsWith(".sciencedirect.com", StringComparison.OrdinalIgnoreCase)
                    || articleSource.Host.Equals("springer.com", StringComparison.OrdinalIgnoreCase)
                    || articleSource.Host.EndsWith(".springer.com", StringComparison.OrdinalIgnoreCase)
                    || articleSource.Host.Equals("springernature.com", StringComparison.OrdinalIgnoreCase)
                    || articleSource.Host.EndsWith(".springernature.com", StringComparison.OrdinalIgnoreCase)))
            {
                SetWaitingForUser(job, "No PDF for the current publisher article was found. The page will be checked again.",
                    null, JobStatus.ManualAttention, "No PDF candidate found");
                return;
            }

            string js = @"(() => {
                const links = [...document.querySelectorAll('a')].map(a => ({href:a.href||'', text:(a.innerText||a.textContent||'').trim()}));
                function score(x) {
                    const h=x.href.toLowerCase(), t=x.text.toLowerCase(); let s=0;
                    if (t.includes('download pdf')) s+=100;
                    if (t === 'pdf' || t.includes('view pdf') || t.includes('full text pdf')) s+=70;
                    if (h.endsWith('.pdf')) s+=80;
                    if (h.includes('.pdf?') || h.includes('/pdf/') || h.includes('/pdfft') || h.includes('pdfdownload')) s+=60;
                    if (h.includes('download') && t.includes('pdf')) s+=50;
                    return s;
                }
                links.forEach(x=>x.s=score(x)); links.sort((a,b)=>b.s-a.s);
                return links.length && links[0].s>0 ? links[0].href : '';
            })();";

            string raw = await web.CoreWebView2.ExecuteScriptAsync(js);
            string candidate = JsonSerializer.Deserialize<string>(raw) ?? "";
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                job.PdfUrl = candidate;
                RefreshRow(job);
                lblCurrent.Text = "PDF candidate found; requesting it as a browser download...";

                // Prefer a browser-context fetch so cookies/session/SSO already held by WebView2
                // are reused. Converting the authenticated response to a Blob and clicking a
                // download anchor causes DownloadStarting instead of navigating into the PDF viewer.
                bool triggered = await TryTriggerPdfDownloadInPageAsync(candidate, job);
                if (triggered)
                {
                    return;
                }

                // Fallback: navigate directly. With V4's PDF-viewer disabling preference/flag,
                // a PDF navigation should become a normal WebView2 download.
                suppressNavigationHandling = true;
                try
                {
                    web.CoreWebView2.Navigate(candidate);
                }
                finally
                {
                    await Task.Delay(1500);
                    suppressNavigationHandling = false;
                }
                return;
            }

            if (await ApplyPageDecisionAsync(job)) return;
            SetWaitingForUser(job,
                "No PDF link or recognizable login/bot state found. The page will be re-checked automatically before it is deferred.",
                null, JobStatus.ManualAttention, "No PDF candidate found");
        }
        catch (Exception ex)
        {
            if (await ApplyPageDecisionAsync(job)) return;
            SetWaitingForUser(job,
                "Manual attention required. The page will be re-checked automatically; unresolved items will be deferred.",
                ex.Message, JobStatus.ManualAttention, "Page analysis/download error");
        }
    }

    private void Web_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        if (currentIndex < 0 || currentIndex >= jobs.Count) return;
        var job = jobs[currentIndex];
        try
        {
            if (IsUnrelatedSpringerPdf(job, e.DownloadOperation.Uri))
            {
                e.Cancel = true;
                LogDiagnostic("rejected-non-main-springer-pdf", job, new { url = e.DownloadOperation.Uri });
                return;
            }
            if (!IsCurrentActiveJob(job) || (nativeDownloadOwners.TryGetValue(e.DownloadOperation.Uri, out var owner) && owner != job))
            {
                e.Cancel = true;
                return;
            }
            var suggested = Path.GetFileName(e.ResultFilePath);
            if (string.IsNullOrWhiteSpace(suggested) || !suggested.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                suggested = $"paper_{job.Id:D5}.pdf";
            }
            string target = FileUtils.DownloadTargetPath(downloadFolder, job.SaveAs, suggested);
            e.ResultFilePath = target;
            e.Handled = true;
            job.FilePath = target;
            job.Status = JobStatus.Downloading;
            StartAttentionDeadline(job);
            job.PdfUrl = e.DownloadOperation.Uri;
            RefreshRow(job);
            lblCurrent.Text = "Downloading: " + Path.GetFileName(target);
            var operation = e.DownloadOperation;
            int attempt = job.Attempts;
            activeNativeDownloads[job] = operation;
            operation.BytesReceivedChanged += (_, _) =>
            {
                if (job.Attempts == attempt && IsCurrentActiveJob(job) && job.Status == JobStatus.Downloading && attentionJobId == job.Id)
                    attentionDeadlineUtc = DateTime.UtcNow.AddSeconds((double)numWaitSeconds.Value);
            };
            operation.StateChanged += (_, _) =>
            {
                if (IsDisposed || Disposing || job.Attempts != attempt || !jobs.Contains(job)) return;
                if (operation.State == CoreWebView2DownloadState.Completed)
                {
                    if (activeNativeDownloads.GetValueOrDefault(job) == operation) activeNativeDownloads.Remove(job);
                    BeginInvoke(new Action(async () => await FinishDownloadAsync(job, target, allowBackground: true)));
                }
                else if (operation.State == CoreWebView2DownloadState.Interrupted)
                {
                    if (activeNativeDownloads.GetValueOrDefault(job) != operation) return;
                    activeNativeDownloads.Remove(job);
                    nativePdfJobs.Remove(job);
                    BeginInvoke(new Action(async () =>
                    {
                        if (job.Attempts != attempt || !IsCurrentActiveJob(job)) return;
                        if (FileUtils.HasCompletePdf(target)) { await FinishDownloadAsync(job, target); return; }
                        SetWaitingForUser(job,
                            "Download interrupted. Fix the page/session and click CONTINUE CURRENT; otherwise it will auto-defer.",
                            "Download interrupted: " + operation.InterruptReason);
                        if (operation.InterruptReason is not (CoreWebView2DownloadInterruptReason.ServerBadContent
                            or CoreWebView2DownloadInterruptReason.ServerCrossOriginRedirect
                            or CoreWebView2DownloadInterruptReason.ServerUnexpectedResponse)) return;
                        if (!interruptedFetchFallbackTried.Add(job)) return;
                        if (File.Exists(target))
                        {
                            try
                            {
                                Directory.CreateDirectory(DiagnosticDirectory);
                                File.Move(target, Path.Combine(DiagnosticDirectory, $"interrupted-{job.Id}-{Guid.NewGuid():N}.bin"));
                                job.FilePath = "";
                            }
                            catch (Exception ex) { LogDiagnostic("interrupted-file-preserve-error", job, ex.Message); return; }
                        }
                        job.PageDecision = "Interrupted native download; trying browser-session fetch once";
                        RefreshRow(job);
                        if (await TryBrowserFetchPdfAsync(operation.Uri, job)) return;
                        if (!IsCurrentActiveJob(job)) return;
                        rejectedPdfUrls.Add(operation.Uri);
                        job.PdfUrl = operation.Uri;
                        web.CoreWebView2.Navigate(operation.Uri);
                    }));
                }
            };
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            e.Handled = true;
            SetWaitingForUser(job,
                "Download could not start. Check the file/session and retry; otherwise it will auto-defer.",
                ex.Message);
        }
    }

    private void Web_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (manualLoginMode || currentIndex < 0 || currentIndex >= jobs.Count) return;
        var job = jobs[currentIndex];
        if (!IsCurrentActiveJob(job) || browserPdfFetchJob is not null || nativePdfJobs.Contains(job)) return;
        try
        {
            string contentType = "";
            try { contentType = e.Response.Headers.GetHeader("Content-Type"); } catch { }
            string disposition = "";
            try { disposition = e.Response.Headers.GetHeader("Content-Disposition"); } catch { }
            if (!IsPdfResponse(e.Request.Uri, contentType, disposition) || IsUnrelatedSpringerPdf(job, e.Request.Uri)) return;
            if (e.Response.StatusCode != 200 && e.Response.StatusCode != 206) return;
            // Inline viewers often expose neither GetContentAsync nor a complete CDP
            // response body. Download the detected URL with the browser instead.
            observedPdfUrls[job] = e.Request.Uri;
            if (job.Status == JobStatus.Downloading || disposition.Contains("attachment", StringComparison.OrdinalIgnoreCase)) return;
            // Leave the response callback before navigating away from an inline PDF.
            // Navigating here can block WebView2 while its PDF viewer is being created.
            string pdfUrl = e.Request.Uri;
            BeginInvoke(new Action(async () => await StartNativePdfDownloadAsync(pdfUrl, job, requireContext: true)));
        }
        catch (Exception ex)
        {
            if (IsCurrentActiveJob(job)) SetWaitingForUser(job, "PDF file download needs attention.", ex.Message);
        }
    }

    private bool IsCurrentActiveJob(DownloadJob job)
        => currentIndex >= 0 && currentIndex < jobs.Count && jobs[currentIndex] == job
            && job.Status is not (JobStatus.Completed or JobStatus.Skipped or JobStatus.Deferred or JobStatus.Failed);

    private static bool IsPdfResponse(string url, string contentType, string disposition)
        => contentType.Split(';')[0].Trim().Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || disposition.Contains(".pdf", StringComparison.OrdinalIgnoreCase)
            || (contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)
                && IsElsevierAssetUrl(url) && IsBinaryPdfUrl(url));

    private static bool IsUnrelatedSpringerPdf(DownloadJob job, string url)
    {
        if (!(job.Doi.StartsWith("10.1007/", StringComparison.OrdinalIgnoreCase)
            || job.Doi.StartsWith("10.1186/", StringComparison.OrdinalIgnoreCase)) || IsBlobUrl(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return true;
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        return !path.Contains(job.Doi, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(path, @"/esm/|/MediaObjects/|MOESM|_ESM\.pdf", RegexOptions.IgnoreCase);
    }

    private bool PrepareDownloadFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(txtFolder.Text)) { MessageBox.Show("Choose a download folder."); return false; }
            downloadFolder = Path.GetFullPath(txtFolder.Text.Trim());
            Directory.CreateDirectory(downloadFolder);
            if (web.CoreWebView2 is not null) web.CoreWebView2.Profile.DefaultDownloadFolderPath = downloadFolder;
            return true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Download folder"); return false; }
    }

    private bool PreserveUntrustedSavedPdf(DownloadJob job)
    {
        if (job.DeferredReason != "Previous PDF is not the main Springer article" || !File.Exists(job.FilePath)) return true;
        try
        {
            string expected = Path.GetFullPath(Path.Combine(downloadFolder, job.SaveAs));
            if (!Path.GetFullPath(job.FilePath).Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
            if (Path.GetFileName(job.FilePath) != job.SaveAs) throw new IOException("Previous filename does not match save_as.");
            Directory.CreateDirectory(DiagnosticDirectory);
            string preserved = Path.Combine(DiagnosticDirectory, $"non-main-{job.Id}-{Guid.NewGuid():N}.pdf");
            File.Move(job.FilePath, preserved);
            LogDiagnostic("preserved-non-main-pdf", job, new { original = job.FilePath, preserved });
            job.FilePath = "";
            return true;
        }
        catch (Exception ex) { job.Error = "Cannot preserve previous PDF: " + ex.Message; RefreshRow(job); return false; }
    }

    private static bool IsScienceDirectWrapperUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("sciencedirect.com", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".sciencedirect.com", StringComparison.OrdinalIgnoreCase))
            && uri.AbsolutePath.Contains("/pdfft", StringComparison.OrdinalIgnoreCase);

    private static bool IsBinaryPdfUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == "https" || uri.Scheme == "http")
            && (uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Contains("/doi/pdf", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Contains("/pdfft", StringComparison.OrdinalIgnoreCase)
                || IsIeeeBinaryPdfUrl(url));

    private static bool IsWileyJob(DownloadJob job)
    {
        var doi = (job.Doi ?? "").Trim();
        var url = (job.Url ?? "").Trim();

        // Wiley Online Library currently serves both native Wiley DOI prefixes
        // (for example 10.1002) and migrated Hindawi content (for example 10.1155).
        // Also trust an already-resolved Wiley Online Library URL regardless of prefix.
        if (doi.StartsWith("10.1002/", StringComparison.OrdinalIgnoreCase)) return true;
        if (doi.StartsWith("10.1155/", StringComparison.OrdinalIgnoreCase)) return true;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith("onlinelibrary.wiley.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWileyLandingUrl(string doi)
        => "https://onlinelibrary.wiley.com/doi/" + doi.Trim();

    private static string GetWileyPdfDirectUrl(string doi)
        => "https://onlinelibrary.wiley.com/doi/pdfdirect/" + doi.Trim();

    private static bool IsIeeePdfUrl(string? url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.Equals("ieeexplore.ieee.org", StringComparison.OrdinalIgnoreCase)) return false;
        return uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("/ielx", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Equals("/stamp/stamp.jsp", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Equals("/stamp/stamppdf.jsp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIeeeDirectPdfJob(DownloadJob job)
    {
        return IsIeeePdfUrl(job.Url);
    }

    private static bool IsIeeeBinaryPdfUrl(string? url)
        => IsIeeePdfUrl(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.Contains("/ielx", StringComparison.OrdinalIgnoreCase));

    private static bool TryGetIeeeArticleNumber(string url, out string articleNumber)
    {
        articleNumber = "";
        if (string.IsNullOrWhiteSpace(url)) return false;

        var m = Regex.Match(url, @"(?:[?&]arnumber=)(\d+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            articleNumber = m.Groups[1].Value;
            return true;
        }

        m = Regex.Match(url, @"/(\d+)\.pdf(?:[?&#]|$)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            articleNumber = m.Groups[1].Value;
            return true;
        }
        return false;
    }

    private static string GetIeeeLandingUrl(string articleNumber)
        => "https://ieeexplore.ieee.org/document/" + articleNumber;

    private async Task<bool> TryClickDisabledPdfDownloadPageAsync(DownloadJob job)
    {
        try
        {
            // This page is rendered by Chromium when --disable-pdf-extension is active.
            // Prefer text/role matching rather than a fixed selector because the internal
            // markup can vary between Edge/WebView2 runtime versions.
            string js = @"(() => {
                const body = (document.body && (document.body.innerText || document.body.textContent) || '').toLowerCase();
                if (!body.includes('pdf reader has been disabled') &&
                    !body.includes('always download pdf files')) return 'NOT_PAGE';

                const els = [...document.querySelectorAll('button, a, [role=button], input[type=button], input[type=submit]')];
                const target = els.find(el => {
                    const t = ((el.innerText || el.textContent || el.value || '') + '').trim().toLowerCase();
                    return t === 'download file' || t.includes('download file');
                });
                if (!target) return 'NO_BUTTON';
                target.click();
                return 'CLICKED';
            })();";

            string raw = await web.CoreWebView2.ExecuteScriptAsync(js);
            string result = JsonSerializer.Deserialize<string>(raw) ?? "";
            if (result == "CLICKED")
            {
                job.Error = "";
                job.Status = JobStatus.Downloading;
                RefreshRow(job);
                lblCurrent.Text = "PDF reader disabled page detected; clicking Download file automatically...";

                // DownloadStarting should fire immediately. If it doesn't, leave the job
                // in LookingForPdf so the timer can retry instead of incorrectly pausing.
                await Task.Delay(1800);
                if (job.Status == JobStatus.Downloading || job.Status == JobStatus.Completed) return true;
                job.Status = JobStatus.LookingForPdf;
                RefreshRow(job);
                pdfLookupTimer.Stop();
                pdfLookupTimer.Start();
                return true;
            }
        }
        catch
        {
            // ExecuteScript can be restricted on some browser-generated pages. In that
            // case the caller continues with the existing PDF-detection fallbacks.
        }
        return false;
    }

    private async Task<bool> TryTriggerPdfDownloadInPageAsync(string candidate, DownloadJob job)
    {
        if (manualLoginMode || !IsCurrentActiveJob(job)) return true;
        if (IsScienceDirectWrapperUrl(candidate))
        {
            // ScienceDirect pdfft may first return HTML which redirects to a signed
            // CDN URL. Let it load rather than saving that HTML with a .pdf name.
            if (web.CoreWebView2.Source == candidate) return false;
            job.PdfUrl = candidate;
            job.Status = JobStatus.Navigating;
            job.PageDecision = "Opening ScienceDirect PDF redirect";
            RefreshRow(job);
            StartAttentionDeadline(job);
            web.CoreWebView2.Navigate(candidate);
            return true;
        }
        if (IsBlobUrl(candidate) || IsBinaryPdfUrl(candidate)) return await StartNativePdfDownloadAsync(candidate, job);
        if (IsIeeePdfUrl(candidate))
        {
            if (IsIeeeBinaryPdfUrl(candidate)) return await StartNativePdfDownloadAsync(candidate, job);
            if (job.Status == JobStatus.Completed || web.CoreWebView2.Source == candidate) return false;
            job.PdfUrl = candidate;
            job.Status = JobStatus.Navigating;
            job.PageDecision = "Opening IEEE PDF in browser";
            job.Error = "";
            RefreshRow(job);
            StartAttentionDeadline(job);
            lblCurrent.Text = $"IEEE: opening PDF in browser (timeout {(int)numWaitSeconds.Value}s)...";
            web.CoreWebView2.Navigate(candidate);
            return true;
        }
        return await TryBrowserFetchPdfAsync(candidate, job);
    }

    private async Task<bool> TryBrowserFetchPdfAsync(string candidate, DownloadJob job)
    {
        if (manualLoginMode || !IsCurrentActiveJob(job)) return true;
        if (browserPdfFetchJob is not null) return false;
        browserPdfFetchJob = job;
        try
        {
            string js = BrowserPdfScript.Create(candidate, string.IsNullOrWhiteSpace(job.SaveAs) ? $"paper_{job.Id:D5}.pdf" : job.SaveAs, (int)numWaitSeconds.Value);
            // ExecuteScriptAsync serializes a Promise rather than awaiting its value.
            // CDP explicitly awaits fetch completion and returns the resulting string.
            string raw = await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = js, awaitPromise = true, returnByValue = true, userGesture = true }))
                .WaitAsync(TimeSpan.FromSeconds((double)numWaitSeconds.Value + 2));
            if (currentIndex < 0 || currentIndex >= jobs.Count || jobs[currentIndex] != job) return true;
            using var doc = JsonDocument.Parse(raw);
            string result = doc.RootElement.TryGetProperty("result", out var value)
                && value.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "" : "";
            if (result == "OK" || job.Status is JobStatus.Downloading or JobStatus.Completed) return true;
            job.Error = string.IsNullOrWhiteSpace(result) ? "Browser-context PDF download did not start." : result;
            RefreshRow(job);
            return false;
        }
        catch (Exception ex)
        {
            if (job.Status == JobStatus.Completed) return true;
            job.Error = "Browser-context download failed: " + ex.Message;
            RefreshRow(job);
            return false;
        }
        finally { if (browserPdfFetchJob == job) browserPdfFetchJob = null; }
    }

    private static void TryRestorePdfViewerPreference(string userDataFolder)
    {
        // Undo the PDF-disabled preference written by earlier releases while preserving
        // the existing login cookies and profile. Failure is non-fatal.
        try
        {
            string[] candidates =
            {
                Path.Combine(userDataFolder, "EBWebView", "Default", "Preferences"),
                Path.Combine(userDataFolder, "Default", "Preferences")
            };

            foreach (var prefPath in candidates)
            {
                if (!File.Exists(prefPath)) continue;
                JsonNode? root = JsonNode.Parse(File.ReadAllText(prefPath));
                if (root is not JsonObject obj) continue;
                var plugins = obj["plugins"] as JsonObject ?? new JsonObject();
                plugins["always_open_pdf_externally"] = false;
                obj["plugins"] = plugins;
                File.WriteAllText(prefPath, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            }
        }
        catch { }
    }

    private static string ExtractPdfFileName(string contentDisposition, string uri, int id)
    {
        string name = "";
        if (!string.IsNullOrWhiteSpace(contentDisposition))
        {
            foreach (var part in contentDisposition.Split(';'))
            {
                var p = part.Trim();
                if (p.StartsWith("filename*=", StringComparison.OrdinalIgnoreCase))
                {
                    name = p[(p.IndexOf('=') + 1)..].Trim().Trim('"');
                    int apos = name.IndexOf("''", StringComparison.Ordinal);
                    if (apos >= 0) name = name[(apos + 2)..];
                    try { name = Uri.UnescapeDataString(name); } catch { }
                    break;
                }
                if (p.StartsWith("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    name = p[(p.IndexOf('=') + 1)..].Trim().Trim('"');
                }
            }
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            try
            {
                var u = new Uri(uri);
                name = Path.GetFileName(u.AbsolutePath);
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            name = $"paper_{id:D5}.pdf";

        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private async Task FinishDownloadAsync(DownloadJob job, string path, bool allowBackground = false)
    {
        bool currentActive = IsCurrentActiveJob(job);
        if (!currentActive)
        {
            if (!allowBackground || !jobs.Contains(job) || job.Status is JobStatus.Skipped or JobStatus.Completed
                || job.FilePath != path || !FileUtils.HasCompletePdf(path)) return;
            nativePdfJobs.Remove(job);
            activeNativeDownloads.Remove(job);
            job.Status = JobStatus.Completed;
            job.Error = ""; job.DeferredReason = ""; job.AttentionReason = "";
            job.PageDecision = "Completed after background download";
            RefreshRow(job); UpdateStats(); SaveStateSnapshot();
            return; // Never advance or otherwise touch another row's queue position.
        }
        if (!File.Exists(path) || !FileUtils.HasPdfMagic(path))
        {
            bool wasNative = nativePdfJobs.Remove(job);
            rejectedPdfUrls.Add(job.PdfUrl);
            nativeDownloadOwners.Remove(job.PdfUrl);
            observedPdfUrls.Remove(job);
            SetWaitingForUser(job,
                "Downloaded file was not a valid PDF. Resolve the publisher/session if needed; otherwise it will auto-defer.",
                "Downloaded file is not a valid PDF.");
            if (wasNative && Uri.TryCreate(job.PdfUrl, UriKind.Absolute, out _))
            {
                // A native download can return a publisher login/challenge HTML page.
                // Show the real URL so the user can complete that step in the browser.
                try { File.Delete(path); } catch { }
                job.FilePath = "";
                web.CoreWebView2.Navigate(job.PdfUrl);
            }
            return;
        }
        else
        {
            nativePdfJobs.Remove(job);
            activeNativeDownloads.Remove(job);
            CancelAttentionDeadline(job);
            job.Status = JobStatus.Completed;
            job.Error = "";
            job.DeferredReason = "";
            job.AttentionReason = "";
            job.PageDecision = "Completed";
        }
        RefreshRow(job);
        UpdateStats();
        SaveStateSnapshot();
        if (job.Status == JobStatus.Completed)
        {
            lblCurrent.Text = "Completed: " + path;
            await Task.Delay(900);
            if (!paused && currentIndex >= 0 && currentIndex < jobs.Count && jobs[currentIndex] == job)
            {
                queueRunning = true;
                await MoveNextAsync();
            }
        }
    }

    private async Task ContinueCurrentAsync()
    {
        if (currentIndex < 0 || currentIndex >= jobs.Count) return;
        var job = jobs[currentIndex];
        CancelAttentionDeadline(job);
        nativePdfJobs.Remove(job);
        job.Status = JobStatus.LookingForPdf;
        job.Error = "";
        job.AttentionReason = "";
        job.PageDecision = "Manual continue / smart re-analysis";
        attemptedPageCandidates.Clear();
        rejectedPdfUrls.Clear();
        RefreshRow(job);
        paused = false;
        queueRunning = true;
        StartAttentionDeadline(job);
        if (IsScienceDirectWrapperUrl(job.PdfUrl) && await TryTriggerPdfDownloadInPageAsync(job.PdfUrl, job)) return;
        if (IsBinaryPdfUrl(job.PdfUrl) && !IsScienceDirectWrapperUrl(job.PdfUrl) && await StartNativePdfDownloadAsync(job.PdfUrl, job)) return;
        await LookupPdfAsync();
    }

    private async Task SkipCurrentAsync()
    {
        if (currentIndex < 0 || currentIndex >= jobs.Count) return;
        CancelAttentionDeadline(jobs[currentIndex]);
        jobs[currentIndex].Status = JobStatus.Skipped;
        jobs[currentIndex].DeferredReason = "Skipped manually";
        RefreshRow(jobs[currentIndex]);
        paused = false;
        queueRunning = true;
        await MoveNextAsync();
    }

    private void ExportResults()
    {
        using var d = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = "paper_download_results.csv" };
        if (d.ShowDialog() == DialogResult.OK) CsvUtils.WriteResults(d.FileName, jobs);
    }

    private void SaveStateSnapshot()
    {
        try
        {
            if (jobs.Count == 0) return;
            var folder = browserProfilePath is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader")
                : Path.Combine(browserProfilePath, "QueueState");
            Directory.CreateDirectory(folder);
            CsvUtils.WriteResults(Path.Combine(folder, "last_results.csv"), jobs);
            var state = new SavedState
            {
                CsvPath = Path.GetFullPath(txtCsv.Text.Trim()),
                Jobs = jobs.Select(j => new SavedJobState
                {
                    Id = j.Id,
                    Doi = j.Doi,
                    Status = j.Status,
                    FilePath = j.FilePath,
                    PdfUrl = j.PdfUrl,
                    Error = j.Error,
                    DeferredReason = j.DeferredReason,
                    AttentionReason = j.AttentionReason,
                    PageDecision = j.PageDecision,
                    Attempts = j.Attempts
                }).ToList()
            };
            File.WriteAllText(Path.Combine(folder, "state_v51.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void RestoreStateSnapshot()
    {
        try
        {
            var folder = browserProfilePath is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader")
                : Path.Combine(browserProfilePath, "QueueState");
            var path = Path.Combine(folder, "state_v51.json");
            if (!File.Exists(path) || string.IsNullOrWhiteSpace(txtCsv.Text)) return;
            var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path));
            if (state is null) return;
            if (!string.Equals(Path.GetFullPath(state.CsvPath), Path.GetFullPath(txtCsv.Text.Trim()), StringComparison.OrdinalIgnoreCase)) return;

            var byDoi = state.Jobs.Where(x => !string.IsNullOrWhiteSpace(x.Doi)).GroupBy(x => x.Doi, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var byId = state.Jobs.ToDictionary(x => x.Id, x => x);
            foreach (var job in jobs)
            {
                SavedJobState? saved = null;
                if (!string.IsNullOrWhiteSpace(job.Doi)) byDoi.TryGetValue(job.Doi, out saved);
                if (saved is null) byId.TryGetValue(job.Id, out saved);
                if (saved is null) continue;

                job.Attempts = saved.Attempts;
                job.PdfUrl = saved.PdfUrl;
                job.FilePath = saved.FilePath;
                job.Error = saved.Error;
                job.DeferredReason = saved.DeferredReason;
                job.AttentionReason = saved.AttentionReason;
                job.PageDecision = saved.PageDecision;
                if (saved.Status is JobStatus.Completed or JobStatus.Deferred && !string.IsNullOrWhiteSpace(saved.PdfUrl)
                    && IsUnrelatedSpringerPdf(job, saved.PdfUrl))
                {
                    job.Status = JobStatus.Deferred;
                    job.DeferredReason = "Previous PDF is not the main Springer article";
                    job.Error = "Previous PDF URL identifies a reference or supplementary file. Retry will preserve it and download the main article.";
                    job.PageDecision = "Previous PDF requires article identity correction";
                }
                else if (saved.Status == JobStatus.Completed && !string.IsNullOrWhiteSpace(job.SaveAs)
                    && !Path.GetFileName(saved.FilePath).Equals(job.SaveAs, StringComparison.OrdinalIgnoreCase))
                {
                    job.Status = JobStatus.Deferred;
                    job.DeferredReason = "Previous download filename does not match save_as";
                }
                else if (saved.Status == JobStatus.Completed && File.Exists(saved.FilePath) && FileUtils.HasPdfMagic(saved.FilePath))
                    job.Status = JobStatus.Completed;
                else if (saved.Status is JobStatus.Deferred or JobStatus.Downloading
                    && !string.IsNullOrWhiteSpace(job.SaveAs)
                    && Path.GetFileName(saved.FilePath).Equals(job.SaveAs, StringComparison.OrdinalIgnoreCase)
                    && FileUtils.HasCompletePdf(saved.FilePath)
                    && Uri.UnescapeDataString(saved.PdfUrl).Contains(job.Doi, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(job.Doi))
                {
                    job.Status = JobStatus.Completed;
                    job.Error = ""; job.DeferredReason = ""; job.AttentionReason = "";
                    job.PageDecision = "Recovered completed PDF from previous download";
                }
                else if (saved.Status is JobStatus.Navigating or JobStatus.LookingForPdf or JobStatus.Downloading || NeedsUserAction(saved.Status))
                {
                    job.Status = JobStatus.Deferred;
                    job.DeferredReason = "Interrupted previous session";
                }
                else
                    job.Status = saved.Status;
            }
        }
        catch { }
    }

    private void RefreshRow(DownloadJob job)
    {
        LogJobState(job);
        var index = jobs.IndexOf(job);
        if (index >= 0) jobs.ResetItem(index);
        grid.Refresh();

        // Keep the row that is currently being processed selected and visible.
        // This makes the grid follow the download queue automatically.
        if (index == currentIndex)
            SelectAndScrollToCurrentRow();

        UpdateStats();
    }

    private void SelectAndScrollToCurrentRow()
    {
        if (currentIndex < 0 || currentIndex >= jobs.Count) return;

        void ApplySelection()
        {
            if (currentIndex < 0 || currentIndex >= jobs.Count) return;
            var row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ReferenceEquals(r.DataBoundItem, jobs[currentIndex]));
            if (row is null) return; // The running job may be outside the user's filtered view.

            grid.ClearSelection();
            row.Selected = true;

            // Set CurrentCell so the row gets the same active-row indicator as a manual click.
            if (row.Cells.Count > 0)
                grid.CurrentCell = row.Cells[0];

            // Keep a little context above the active row when possible.
            var firstVisible = Math.Max(0, row.Index - 2);
            try
            {
                grid.FirstDisplayedScrollingRowIndex = firstVisible;
            }
            catch
            {
                // Ignore transient layout errors while the grid is refreshing.
            }
        }

        if (grid.InvokeRequired)
            grid.BeginInvoke((Action)ApplySelection);
        else
            ApplySelection();
    }

    private void UpdatePublisherSummary()
    {
        if (jobs.Count == 0) { lblPublisherSummary.Text = ""; return; }
        var parts = jobs.GroupBy(j => j.PublisherKey)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                int ok = g.Count(x => x.Status == JobStatus.Completed);
                int attention = g.Count(x => NeedsUserAction(x.Status) || x.Status == JobStatus.Deferred);
                int pending = g.Count(x => x.Status == JobStatus.Pending);
                string state = suspendedPublishers.Contains(g.Key) ? "SUSPENDED" : attention > 0 ? "ATTENTION" : ok > 0 ? "READY" : "PENDING";
                return $"{g.Key}:{state} {ok}/{g.Count()}" + (attention > 0 ? $" (deferred/wait {attention})" : "");
            });
        lblPublisherSummary.Text = "Publishers  |  " + string.Join("   |   ", parts.Take(10)) + (parts.Count() > 10 ? "   |   ..." : "");
    }

    private void UpdateStats()
    {
        int completed = jobs.Count(x => x.Status == JobStatus.Completed);
        int failed = jobs.Count(x => x.Status == JobStatus.Failed);
        int waiting = jobs.Count(x => NeedsUserAction(x.Status));
        int deferred = jobs.Count(x => x.Status == JobStatus.Deferred);
        int skipped = jobs.Count(x => x.Status == JobStatus.Skipped);
        int pending = jobs.Count(x => x.Status == JobStatus.Pending);
        int noUrl = jobs.Count(x => x.Status == JobStatus.NoUrl);
        int direct = jobs.Count(x => x.IsDirectPdfCandidate);
        lblStats.Text = $"Total: {jobs.Count}   Completed: {completed}   Pending: {pending}   Waiting: {waiting}   Deferred: {deferred}   Failed: {failed}   Skipped: {skipped}   No URL: {noUrl}   Direct PDF: {direct}";
        UpdatePublisherSummary();
    }
}
