using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private readonly string diagnosticSession = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
    private readonly Dictionary<DownloadJob, string> lastDiagnosticStates = new();
    private string DiagnosticDirectory => Path.Combine(ProfileDataPath, "Diagnostics", diagnosticSession);
    private DownloadJob? DiagnosticJob => !manualLoginMode && currentIndex >= 0 && currentIndex < jobs.Count ? jobs[currentIndex] : null;

    private void InitializeDiagnosticsUi(FlowLayoutPanel buttons)
    {
        var capture = new Button { Text = "SAVE WEBVIEW SNAPSHOT", AutoSize = true };
        var open = new Button { Text = "OPEN LOG FOLDER", AutoSize = true };
        capture.Click += async (_, _) => await SaveWebViewSnapshotAsync();
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(DiagnosticDirectory);
                Process.Start(new ProcessStartInfo(DiagnosticDirectory) { UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Log folder"); }
        };
        buttons.Controls.Add(capture);
        buttons.Controls.Add(open);
        buttons.WrapContents = true;

        var menu = new ContextMenuStrip();
        ToolStripMenuItem Add(string label, Func<string?> value)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => CopyDiagnosticText(value());
            menu.Items.Add(item);
            return item;
        }
        DownloadJob? menuRow = null;
        string? menuCell = null;
        DownloadJob? Row() => menuRow;
        var values = new Dictionary<ToolStripMenuItem, Func<string?>>();
        void CopyItem(string name, Func<string?> get) => values.Add(Add(name, get), get);
        CopyItem("Copy cell", () => menuCell);
        CopyItem("Copy DOI", () => Row()?.Doi);
        CopyItem("Copy DOI URL", () => string.IsNullOrWhiteSpace(Row()?.Doi) ? "" : "https://doi.org/" + Row()!.Doi);
        CopyItem("Copy selected URL", () => Row()?.Url);
        CopyItem("Copy PDF URL", () => Row()?.PdfUrl);
        CopyItem("Copy title", () => Row()?.Title);
        CopyItem("Copy file path", () => Row()?.FilePath);
        CopyItem("Copy error / decision", () => Row() is { } j ? string.Join(Environment.NewLine,
            new[] { j.Error, j.PageDecision, j.AttentionReason }.Where(s => !string.IsNullOrWhiteSpace(s))) : "");
        menu.Opening += (_, e) =>
        {
            // Hold the clicked target even if the running queue refreshes selection
            // while this menu is open.
            menuRow = grid.CurrentRow?.DataBoundItem as DownloadJob;
            menuCell = grid.CurrentCell?.FormattedValue?.ToString();
            e.Cancel = grid.CurrentRow is null;
            foreach (var entry in values) entry.Key.Enabled = !string.IsNullOrWhiteSpace(entry.Value());
        };
        // Select the actual right-clicked row/cell before the menu reads any values.
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            grid.ClearSelection();
            grid.CurrentCell = grid.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            grid.Rows[e.RowIndex].Selected = true;
        };
        grid.ContextMenuStrip = menu;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
    }

    private void CopyDiagnosticText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { Clipboard.SetText(value); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Copy to clipboard"); }
    }

    private void InitializeDiagnosticsBrowser()
    {
        LogDiagnostic("session-start", null, new { version = "5.3.2", browser = web.CoreWebView2.Environment.BrowserVersionString,
            profileId = ProfileId, accessMode = CurrentAccessMode.ToString() });
        web.CoreWebView2.NavigationStarting += (_, e) =>
            LogDiagnostic("navigation-start", DiagnosticJob, new { e.NavigationId, e.Uri, e.IsRedirected, e.Cancel });
        web.CoreWebView2.NavigationCompleted += (_, e) =>
            LogDiagnostic("navigation-complete", navigationOwners.GetValueOrDefault(e.NavigationId),
                new { e.NavigationId, e.IsSuccess, error = e.WebErrorStatus.ToString() });
        web.CoreWebView2.WebResourceResponseReceived += (_, e) =>
        {
            try
            {
                var mime = e.Response.Headers.Contains("Content-Type") ? e.Response.Headers.GetHeader("Content-Type") : "";
                var responseOwner = nativeDownloadOwners.GetValueOrDefault(e.Request.Uri) ?? navigationUrlOwners.GetValueOrDefault(e.Request.Uri);
                if (responseOwner is not null && !jobs.Contains(responseOwner)) responseOwner = null;
                if (responseOwner is not null || e.Request.Uri == web.CoreWebView2.Source || mime.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                    || IsBinaryPdfUrl(e.Request.Uri))
                {
                    LogDiagnostic("http-response", responseOwner ?? DiagnosticJob, new { url = e.Request.Uri, status = e.Response.StatusCode, contentType = mime });
                    if (e.Response.StatusCode is 403 or 429)
                    {
                        var owner = responseOwner ?? (e.Request.Uri == web.CoreWebView2.Source ? DiagnosticJob : null);
                        if (owner is not null) ApplyPublisherBackoff(owner.PublisherKey, "HTTP " + e.Response.StatusCode + (e.Response.StatusCode == 403 ? " Forbidden" : ""));
                    }
                }
            }
            catch (Exception ex) { LogDiagnostic("diagnostic-error", DiagnosticJob, ex.Message); }
        };
        web.CoreWebView2.DownloadStarting += (_, e) =>
        {
            var operation = e.DownloadOperation;
            var owner = DiagnosticJob;
            LogDiagnostic("download-start", owner, new { operation.Uri, operation.MimeType, e.Cancel, e.ResultFilePath });
            operation.StateChanged += (_, _) => LogDiagnostic("download-state", owner,
                new { operation.Uri, state = operation.State.ToString(), reason = operation.InterruptReason.ToString(), operation.BytesReceived, operation.TotalBytesToReceive });
        };
        web.CoreWebView2.ContextMenuRequested += (_, e) =>
        {
            var url = web.CoreWebView2.Source;
            var item = web.CoreWebView2.Environment.CreateContextMenuItem("Copy current page URL", null, CoreWebView2ContextMenuItemKind.Command);
            item.CustomItemSelected += (_, _) => CopyDiagnosticText(url);
            e.MenuItems.Add(item);
        };
    }

    private void LogDiagnostic(string kind, DownloadJob? job, object? detail)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            var line = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind, profile_id = ProfileId,
                access_mode = CurrentAccessMode.ToString(), run_id = runId, csv_sha256 = csvSha256,
                job = job is null ? null : new { job.Id, job.Doi, job.Url, job.Attempts, job.PublisherKey, job.SaveAs, job.IsOpenAccess, status = job.StatusText,
                    job.PdfUrl, job.PageDecision, job.Error, job.FilePath }, detail });
            File.AppendAllText(Path.Combine(DiagnosticDirectory, "events.jsonl"), line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex) { lblStats.Text = "Cannot write diagnostics: " + ex.Message; }
    }

    private void LogJobState(DownloadJob job)
    {
        var state = JsonSerializer.Serialize(new { job.Status, job.PageDecision, job.Error, job.Attempts, job.PdfUrl, job.FilePath });
        if (lastDiagnosticStates.TryGetValue(job, out var old) && old == state) return;
        lastDiagnosticStates[job] = state;
        LogDiagnostic("job-state", job, null);
    }

    private async Task SaveWebViewSnapshotAsync()
    {
        if (!initialized) return;
        var job = DiagnosticJob;
        var attempt = job?.Attempts;
        var url = web.CoreWebView2.Source;
        var page = await AnalyzeCurrentPageAsync();
        if (url != web.CoreWebView2.Source || job != DiagnosticJob || attempt != job?.Attempts)
        {
            LogDiagnostic("snapshot-discarded", job, "Page/job changed during capture; retry on a paused queue.");
            lblCurrent.Text = "Page changed during capture. Pause the queue and save again.";
            return;
        }
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            var path = Path.Combine(DiagnosticDirectory, $"webview-{DateTime.Now:HHmmss-fffffff}-row-{job?.Id ?? 0}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, jobId = job?.Id, url, page },
                new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            LogDiagnostic("manual-snapshot", job, new { path });
            lblCurrent.Text = "WebView snapshot saved: " + path;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Save WebView snapshot"); }
    }
}
