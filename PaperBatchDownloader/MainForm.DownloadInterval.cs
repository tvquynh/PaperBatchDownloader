using System.Diagnostics;
using System.Text.Json;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private readonly NumericUpDown numDownloadInterval = new()
    {
        Minimum = 0, Maximum = 1440, DecimalPlaces = 2, Increment = 0.5m, Value = 0, Width = 85
    };
    private long? lastPaperCompletedTimestamp;
    private string DownloadIntervalSettingsPath => Path.Combine(ProfileDataPath, "download_interval.json");
    private sealed class DownloadIntervalSettings
    {
        public decimal Minutes { get; set; }
    }

    private void InitializeDownloadInterval(TableLayoutPanel top)
    {
        try
        {
            if (File.Exists(DownloadIntervalSettingsPath))
            {
                var settings = JsonSerializer.Deserialize<DownloadIntervalSettings>(File.ReadAllText(DownloadIntervalSettingsPath));
                if (settings is not null)
                    numDownloadInterval.Value = Math.Clamp(settings.Minutes, numDownloadInterval.Minimum, numDownloadInterval.Maximum);
            }
        }
        catch (Exception ex) { LogDiagnostic("download-interval-load-error", null, ex.Message); }

        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        row.Controls.AddRange(new Control[]
        {
            new Label { Text = "Nghỉ giữa các paper (phút):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) },
            numDownloadInterval,
            new Label { Text = "0 = tải liên tục; > 0 = tải lần lượt, nghỉ sau khi tải xong. PAUSE để tạm dừng.", AutoSize = true, Padding = new Padding(8, 5, 0, 0) }
        });
        top.RowCount = 5;
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(row, 0, 3);
        top.SetColumnSpan(row, 4);
        numDownloadInterval.ValueChanged += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(ProfileDataPath);
                File.WriteAllText(DownloadIntervalSettingsPath, JsonSerializer.Serialize(new DownloadIntervalSettings { Minutes = numDownloadInterval.Value }));
            }
            catch (Exception ex) { LogDiagnostic("download-interval-save-error", null, ex.Message); }
            await PumpQueueAsync();
        };
    }

    private bool WaitForDownloadInterval()
    {
        if (numDownloadInterval.Value == 0) return false;
        // Do not delay finishing the pass after its final paper.
        if (!queuePlan.Any(i => i >= 0 && i < jobs.Count && jobs[i].Status == JobStatus.Pending)) return false;
        if (HasActiveTransfers)
        {
            pipelineWaiting = true;
            lblCurrent.Text = "Chờ paper đang tải hoàn tất trước khi nghỉ và tải bài tiếp theo.";
            return true;
        }
        if (lastPaperCompletedTimestamp is not { } timestamp) return false;
        double seconds = (double)numDownloadInterval.Value * 60 - Stopwatch.GetElapsedTime(timestamp).TotalSeconds;
        if (seconds <= 0) return false;
        pipelineWaiting = true;
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(seconds));
        lblCurrent.Text = $"Đang nghỉ giữa các paper — còn {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}. PAUSE để tạm dừng.";
        return true;
    }
}
