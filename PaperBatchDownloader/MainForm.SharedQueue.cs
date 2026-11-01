using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private readonly string? sharedQueueRoot;
    private string? sharedStateFolder;
    private string queueCsvHash = "";
    private string queueOutputFolder = "";
    private string QueueStateFolder => sharedStateFolder ?? Path.Combine(ProfileDataPath, "QueueState");

    private static string NormalPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
    private static bool SamePath(string left, string right) => NormalPath(left).Equals(NormalPath(right), StringComparison.OrdinalIgnoreCase);

    private void InitializeQueueStorage()
    {
        queueCsvHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(txtCsv.Text))).ToLowerInvariant();
        queueOutputFolder = string.IsNullOrWhiteSpace(txtFolder.Text) ? "" : NormalPath(txtFolder.Text);
        sharedStateFolder = null;
        if (sharedQueueRoot is null || queueOutputFolder.Length == 0) return;
        string identity = NormalPath(txtCsv.Text).ToUpperInvariant() + "\n" + queueCsvHash + "\n" + queueOutputFolder.ToUpperInvariant();
        sharedStateFolder = Path.Combine(sharedQueueRoot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant());
    }

    private string SelectedOutputFolder => string.IsNullOrWhiteSpace(txtFolder.Text) ? "" : NormalPath(txtFolder.Text);
    private bool QueueInputsMatch() => queueCsvHash.Length == 0
        || queueOutputFolder.Equals(SelectedOutputFolder, StringComparison.OrdinalIgnoreCase);

    private bool ReloadForChangedOutputFolder()
    {
        try
        {
            if (loadedCsvPath.Length > 0 && !QueueInputsMatch())
            {
                PauseQueue();
                LoadCsv();
            }
            return QueueInputsMatch();
        }
        catch (Exception ex) { lblCurrent.Text = "Thư mục tải không hợp lệ: " + ex.Message; return false; }
    }

    private void LoadQueueAfterAccessSwitch()
    {
        if (!File.Exists(txtCsv.Text) || string.IsNullOrWhiteSpace(txtFolder.Text)) return;
        LoadCsv();
        if (jobs.Count == 0 || !PrepareDownloadFolder()) return;
        foreach (var job in jobs.Where(j => j.Status != JobStatus.Completed && j.Status != JobStatus.NoUrl))
        {
            if (!PreserveUntrustedSavedPdf(job)) continue;
            job.Status = JobStatus.Pending;
            job.PdfUrl = ""; // A signed publisher URL from the other session may have expired.
            job.Error = ""; job.AttentionReason = ""; job.DeferredReason = "";
            job.PageDecision = "Chờ thử bằng chế độ truy cập mới";
            RefreshRow(job);
        }
        SaveStateSnapshot();
        UpdateStats(); UpdatePublisherSummary();
        lblCurrent.Text = $"Đã giữ {jobs.Count(j => j.Status == JobStatus.Completed)}/{jobs.Count} bài đã tải. Chuẩn bị đăng nhập nếu cần, bấm START để tải phần còn thiếu.";
    }

    private void RestoreLegacyCompletedPdfs()
    {
        if (sharedQueueRoot is null || queueOutputFolder.Length == 0) return;
        // Read old per-profile state only to recover verified files in this output folder.
        // Never import old failures, cookies, signed links, or a different output folder.
        var legacyPaths = new[] {
            Path.Combine(DefaultProfilePath(AccessMode.PtitIp), "QueueState", "state_v51.json"),
            Path.Combine(DefaultProfilePath(AccessMode.VnuAccount), "QueueState", "state_v51.json") };
        if (browserProfilePath != Path.Combine(ProfileDataPath, "Browser"))
            legacyPaths = new[] { Path.Combine(ProfileDataPath, "QueueState", "state_v51.json") };
        foreach (string path in legacyPaths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                var state = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(path));
                if (state is null || !SamePath(state.CsvPath, txtCsv.Text)) continue;
                if (state.CsvSha256.Length > 0 && state.CsvSha256 != queueCsvHash) continue;
                foreach (var job in jobs.Where(j => j.Status != JobStatus.Completed && j.Doi.Length > 0))
                {
                    if (job.DeferredReason == "Previous PDF is not the main Springer article") continue;
                    var untrusted = state.Jobs.FirstOrDefault(j => j.Doi.Equals(job.Doi, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(j.FilePath) && File.Exists(j.FilePath)
                        && SamePath(Path.GetDirectoryName(j.FilePath)!, queueOutputFolder)
                        && IsUnrelatedSpringerPdf(job, j.PdfUrl));
                    if (untrusted is not null)
                    {
                        job.Status = JobStatus.Deferred; job.FilePath = untrusted.FilePath; job.PdfUrl = untrusted.PdfUrl;
                        job.DeferredReason = "Previous PDF is not the main Springer article";
                        job.Error = "Previous Springer PDF requires article identity correction.";
                        continue;
                    }
                    var saved = state.Jobs.FirstOrDefault(j => j.Doi.Equals(job.Doi, StringComparison.OrdinalIgnoreCase)
                        && j.Status == JobStatus.Completed && !string.IsNullOrWhiteSpace(j.FilePath)
                        && SamePath(Path.GetDirectoryName(j.FilePath)!, queueOutputFolder)
                        && (job.SaveAs.Length == 0 || Path.GetFileName(j.FilePath).Equals(job.SaveAs, StringComparison.OrdinalIgnoreCase))
                        && !IsUnrelatedSpringerPdf(job, j.PdfUrl) && FileUtils.HasCompletePdf(j.FilePath));
                    if (saved is null) continue;
                    job.Status = JobStatus.Completed; job.FilePath = saved.FilePath;
                    job.PdfUrl = saved.PdfUrl; job.Error = ""; job.DeferredReason = ""; job.AttentionReason = "";
                    job.PageDecision = "Recovered completed PDF from previous profile";
                }
            }
            catch (Exception ex) { LogDiagnostic("legacy-queue-read-error", null, ex.Message); }
        }
    }
}
