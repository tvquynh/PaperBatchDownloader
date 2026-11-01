using Microsoft.Web.WebView2.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private readonly NumericUpDown numTransfers = new() { Minimum = 1, Maximum = 4, Value = 2, Width = 45 };
    private readonly NumericUpDown numPublisherTransfers = new() { Minimum = 1, Maximum = 2, Value = 1, Width = 45 };
    private readonly System.Windows.Forms.Timer pipelineTimer = new() { Interval = 500 };
    private readonly HashSet<DownloadJob> directHttpJobs = new();
    private readonly HashSet<DownloadJob> nativeFinalizations = new();
    private readonly Dictionary<DownloadJob, DateTimeOffset> jobStartedUtc = new();
    private readonly Dictionary<DownloadJob, DateTimeOffset> downloadStartedUtc = new();
    private readonly Dictionary<DownloadJob, DateTimeOffset> downloadLastProgressUtc = new();
    private readonly Dictionary<string, DateTimeOffset> publisherCooldown = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> publisherFailures = new(StringComparer.OrdinalIgnoreCase);
    private bool movingQueue;
    private bool queuePumpRequested;
    private bool pipelineWaiting;
    private bool pipelineTickBusy;
    private string? runId;
    private string csvSha256 = "";
    private string csvName = "";
    private string loadedCsvPath = "";
    private DateTimeOffset runStartedUtc;
    private bool runFinished;
    private readonly HashSet<int> completedAtRunStart = new();

    private void StartPipelineTimer()
    {
        pipelineTimer.Tick += async (_, _) =>
        {
            if (pipelineTickBusy || IsDisposed || Disposing) return;
            pipelineTickBusy = true;
            try
            {
                foreach (var entry in activeNativeDownloads.ToArray())
                    if (entry.Value.State == CoreWebView2DownloadState.InProgress
                        && downloadLastProgressUtc.TryGetValue(entry.Key, out var last)
                        && (DateTimeOffset.UtcNow - last).TotalSeconds > (double)numWaitSeconds.Value)
                    {
                        downloadLastProgressUtc[entry.Key] = DateTimeOffset.UtcNow;
                        LogDiagnostic("transfer-idle-cancel", entry.Key, new { idleLimitSeconds = (int)numWaitSeconds.Value });
                        entry.Value.Cancel();
                    }
                if (logoutRequested && !HasActiveTransfers) await CompleteVnuLogoutAsync();
                else await PumpQueueAsync();
            }
            catch (Exception ex) { LogDiagnostic("pipeline-error", null, ex.Message); }
            finally { pipelineTickBusy = false; }
        };
        pipelineTimer.Start();
    }

    private bool CanSchedulePublisher(string publisher)
    {
        if (activeNativeDownloads.Count >= (int)numTransfers.Value) return false;
        if (activeNativeDownloads.Keys.Count(j => j.PublisherKey.Equals(publisher, StringComparison.OrdinalIgnoreCase)) >= (int)numPublisherTransfers.Value) return false;
        return !publisherCooldown.TryGetValue(publisher, out var until) || until <= DateTimeOffset.UtcNow;
    }

    private async Task PumpQueueAsync()
    {
        if (IsDisposed || Disposing || paused || !queueRunning || manualLoginMode || logoutRequested || logoutInProgress) return;
        if (nativePdfContext is not null || browserPdfFetchJob is not null || directHttpJobs.Count > 0) return;
        bool eligible = pipelineWaiting || currentIndex < 0 || currentIndex >= jobs.Count;
        if (!eligible)
        {
            var current = jobs[currentIndex];
            eligible = current.Status is JobStatus.Completed or JobStatus.Deferred or JobStatus.Failed or JobStatus.Skipped
                || (current.Status == JobStatus.Downloading && activeNativeDownloads.ContainsKey(current));
        }
        if (eligible) await MoveNextAsync();
    }

    private void ApplyPublisherBackoff(string publisher, string reason)
    {
        if (!reason.Contains("Forbidden", StringComparison.OrdinalIgnoreCase) && !reason.Contains("429")
            && !reason.Contains("BotCheck") && !reason.Contains("NetworkFailed")) return;
        if (reason == "BotCheck" && publisherCooldown.TryGetValue(publisher, out var existing) && existing > DateTimeOffset.UtcNow) return;
        int failures = publisherFailures.GetValueOrDefault(publisher) + 1;
        publisherFailures[publisher] = failures;
        int seconds = Math.Min(120, 15 * (1 << Math.Min(3, failures - 1)));
        publisherCooldown[publisher] = DateTimeOffset.UtcNow.AddSeconds(seconds);
        LogDiagnostic("publisher-backoff", null, new { publisher, reason, seconds });
    }

    private void PreserveInterruptedTransfer(DownloadJob job, string path)
    {
        if (!File.Exists(path)) { job.FilePath = ""; return; }
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            File.Move(path, Path.Combine(DiagnosticDirectory, $"interrupted-{job.Id}-{Guid.NewGuid():N}.bin"));
            job.FilePath = "";
        }
        catch (Exception ex) { LogDiagnostic("interrupted-file-preserve-error", job, ex.Message); }
    }

    private void RegisterLoadedBatch()
    {
        loadedCsvPath = Path.GetFullPath(txtCsv.Text);
        csvName = Path.GetFileName(txtCsv.Text);
        csvSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(txtCsv.Text))).ToLowerInvariant();
        runId = null; jobStartedUtc.Clear(); downloadStartedUtc.Clear();
        lastDiagnosticStates.Clear();
        LogDiagnostic("batch-loaded", null, new { csvName, csvSha256, rows = jobs.Count });
        foreach (var job in jobs) LogJobState(job);
    }

    private bool BeginRun(string action)
    {
        if (profileUnavailable || !File.Exists(txtCsv.Text)) return false;
        string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(txtCsv.Text))).ToLowerInvariant();
        if (Path.GetFullPath(txtCsv.Text) != loadedCsvPath || actualHash != csvSha256)
        {
            lblCurrent.Text = "CSV đã đổi đường dẫn/nội dung sau khi nạp. Hãy Load CSV lại trước khi chạy.";
            return false;
        }
        csvName = Path.GetFileName(txtCsv.Text);
        runId = Guid.NewGuid().ToString("N");
        runFinished = false;
        runStartedUtc = DateTimeOffset.UtcNow;
        completedAtRunStart.Clear();
        foreach (var job in jobs.Where(j => j.Status == JobStatus.Completed)) completedAtRunStart.Add(job.Id);
        publisherCooldown.Clear(); publisherFailures.Clear();
        LogDiagnostic("run-start", null, new { action, csvName, csvSha256, rows = jobs.Count,
            maxTransfers = (int)numTransfers.Value, maxPerPublisher = (int)numPublisherTransfers.Value,
            waitSeconds = (int)numWaitSeconds.Value, outputFolder = downloadFolder,
            alreadyCompletedAtStart = completedAtRunStart.Count });
        return true;
    }

    private void RecordDownloadCompletion(DownloadJob job, string path, string completion)
    {
        if (!publisherCooldown.TryGetValue(job.PublisherKey, out var until) || until <= DateTimeOffset.UtcNow)
        {
            publisherFailures.Remove(job.PublisherKey);
            publisherCooldown.Remove(job.PublisherKey);
        }
        long bytes = 0; try { bytes = new FileInfo(path).Length; } catch { }
        var now = DateTimeOffset.UtcNow;
        LogDiagnostic("transfer-complete", job, new { completion, bytes, transport = downloadStartedUtc.ContainsKey(job) ? "webview-native" : "http-direct",
            elapsedMs = jobStartedUtc.TryGetValue(job, out var start) ? (double?)(now - start).TotalMilliseconds : null,
            transferMs = downloadStartedUtc.TryGetValue(job, out var begin) ? (double?)(now - begin).TotalMilliseconds : null });
        downloadLastProgressUtc.Remove(job);
    }

    private void SaveRunSummary(bool finished)
    {
        if (runId is null) return;
        try
        {
            runFinished |= finished;
            Directory.CreateDirectory(DiagnosticDirectory);
            var report = new { runId, profileId = ProfileId, accessMode = CurrentAccessMode.ToString(), csvName, csvSha256,
                finished = runFinished, startedUtc = runStartedUtc, endedUtc = DateTimeOffset.UtcNow,
                elapsedSeconds = (DateTimeOffset.UtcNow - runStartedUtc).TotalSeconds,
                alreadyCompletedAtStart = completedAtRunStart.Count,
                completedDuringRun = jobs.Count(j => j.Status == JobStatus.Completed && !completedAtRunStart.Contains(j.Id)),
                maxTransfers = (int)numTransfers.Value, maxPerPublisher = (int)numPublisherTransfers.Value,
                rows = jobs.Count, statuses = jobs.GroupBy(j => j.StatusText).ToDictionary(g => g.Key, g => g.Count()),
                publishers = jobs.GroupBy(j => j.PublisherKey).ToDictionary(g => g.Key,
                    g => g.GroupBy(j => j.StatusText).ToDictionary(s => s.Key, s => s.Count())),
                items = jobs.Select(j => new { j.Id, j.Doi, j.SaveAs, j.Url, status = j.StatusText, j.Error, j.DeferredReason, j.FilePath }) };
            File.WriteAllText(Path.Combine(DiagnosticDirectory, $"run-summary-{runId}.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (finished) LogDiagnostic("run-complete", null, new { rows = jobs.Count, elapsedSeconds = report.elapsedSeconds });
        }
        catch (Exception ex) { LogDiagnostic("summary-error", null, ex.Message); }
    }
}
