using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace PaperBatchDownloader;

public enum AccessMode { PtitIp, VnuAccount }

public sealed partial class MainForm
{
    public AccessMode CurrentAccessMode { get; }
    public AccessMode? RequestedAccessMode { get; private set; }
    public (string CsvPath, string DownloadFolder)? RequestedInputPaths { get; private set; }
    private string ProfileId => CurrentAccessMode == AccessMode.VnuAccount ? "VNU_ACCOUNT" : "PTIT_IP";
    private string ProfileDataPath { get; }
    private static string DefaultProfilePath(AccessMode mode) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader", "Profiles",
        mode == AccessMode.VnuAccount ? "VNU_ACCOUNT" : "PTIT_IP");
    private readonly ComboBox cmbAccessMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly Button btnLogoutVnu = new() { Text = "Đăng xuất VNU", AutoSize = true };
    private readonly HashSet<DownloadJob> authRoutedJobs = new();
    private readonly HashSet<CoreWebView2DownloadOperation> manualTransfers = new();
    private bool logoutRequested;
    private bool logoutInProgress;
    private bool profileUnavailable;
    private bool HasActiveTransfers => activeNativeDownloads.Values.Any(d => d.State == CoreWebView2DownloadState.InProgress)
        || directHttpJobs.Count > 0 || nativeFinalizations.Count > 0 || browserPdfFetchJob is not null
        || manualTransfers.Any(d => d.State == CoreWebView2DownloadState.InProgress);

    private void InitializeAccessProfiles(FlowLayoutPanel buttons)
    {
        cmbAccessMode.Items.AddRange(new object[] { "PTIT/IP — mạng hiện tại", "VNU — OpenAthens" });
        cmbAccessMode.SelectedIndex = CurrentAccessMode == AccessMode.VnuAccount ? 1 : 0;
        var change = new Button { Text = "Đổi hồ sơ", AutoSize = true };
        change.Click += (_, _) =>
        {
            if (HasActiveTransfers || logoutInProgress || logoutRequested)
            { lblCurrent.Text = "Chờ PDF/đăng xuất hoàn tất trước khi đổi hồ sơ."; return; }
            var target = cmbAccessMode.SelectedIndex == 1 ? AccessMode.VnuAccount : AccessMode.PtitIp;
            if (target == CurrentAccessMode) return;
            PauseQueue();
            manualLoginMode = true;
            SaveStateSnapshot();
            web.CoreWebView2?.Stop();
            RequestedInputPaths = (txtCsv.Text, txtFolder.Text);
            RequestedAccessMode = target;
            LogDiagnostic("profile-switch-requested", null, new { target = target.ToString() });
            Close(); // Program creates a fresh form: no old queue callbacks or browser cookies cross profiles.
        };
        btnLogoutVnu.Enabled = CurrentAccessMode == AccessMode.VnuAccount;
        btnLogoutVnu.Click += async (_, _) => await RequestVnuLogoutAsync();
        buttons.Controls.AddRange(new Control[] { cmbAccessMode, change, btnLogoutVnu,
            new Label { Text = "PDF đồng thời:", AutoSize = true, Padding = new Padding(5, 6, 0, 0) }, numTransfers,
            new Label { Text = "PDF/nguồn:", AutoSize = true, Padding = new Padding(5, 6, 0, 0) }, numPublisherTransfers });
    }

    private string LibraryEntryUrl(LibraryResource resource)
    {
        if (CurrentAccessMode == AccessMode.VnuAccount) return resource.LoginUrl;
        if (resource.Key == "arxiv") return "https://arxiv.org/";
        if (Uri.TryCreate(resource.LoginUrl, UriKind.Absolute, out var library) && library.Host.EndsWith("vnulib.edu.vn", StringComparison.OrdinalIgnoreCase)) return "about:blank";
        if (Uri.TryCreate(resource.LoginUrl, UriKind.Absolute, out var uri) && uri.Host == "go.openathens.net")
        {
            var match = System.Text.RegularExpressions.Regex.Match(uri.Query, @"(?:^\?|&)url=([^&]+)");
            if (match.Success) return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        string direct = PublisherResolver.LoginUrl(resource.Publisher);
        return direct.Length > 0 ? direct : resource.LoginUrl;
    }

    private bool TryRouteVnuArticle(DownloadJob job, string url, out string routed)
    {
        routed = "";
        if (CurrentAccessMode != AccessMode.VnuAccount || authRoutedJobs.Contains(job) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        bool article = job.PublisherKey switch {
            "IEEE" => uri.Host.Equals("ieeexplore.ieee.org", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/document/"),
            "Elsevier" => uri.Host.Equals("www.sciencedirect.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/science/article/"),
            "Springer" => (uri.Host == "link.springer.com" || uri.Host == "link.springernature.com") && uri.AbsolutePath.StartsWith("/article/"),
            "Nature" => (uri.Host == "www.nature.com" || uri.Host == "nature.com") && uri.AbsolutePath.StartsWith("/articles/"),
            _ => false
        };
        if (!article) return false;
        var source = libraryResources.FirstOrDefault(r => MatchesLibraryResource(r, job));
        if (source is null || !Uri.TryCreate(source.LoginUrl, UriKind.Absolute, out var portal) || portal.Host != "go.openathens.net") return false;
        routed = portal.GetLeftPart(UriPartial.Path) + "?url=" + Uri.EscapeDataString(url);
        authRoutedJobs.Add(job);
        LogDiagnostic("institutional-article-route", job, new { publisher = job.PublisherKey, target = url });
        return true;
    }

    private bool ShouldUseAnonymousHttp(DownloadJob job) => job.IsDirectPdfCandidate && !IsWileyJob(job) && !IsIeeeDirectPdfJob(job)
        && (CurrentAccessMode == AccessMode.PtitIp || job.PublisherKey.Equals("arXiv", StringComparison.OrdinalIgnoreCase));

    private void TrackManualTransfer(CoreWebView2DownloadOperation operation)
    {
        manualTransfers.Add(operation);
        operation.StateChanged += (_, _) =>
        {
            if (operation.State != CoreWebView2DownloadState.InProgress) manualTransfers.Remove(operation);
        };
    }

    private async Task RequestVnuLogoutAsync()
    {
        if (CurrentAccessMode != AccessMode.VnuAccount || !initialized || logoutInProgress || logoutRequested) return;
        logoutRequested = true;
        btnLogoutVnu.Enabled = false;
        PauseQueue();
        if (nativePdfContext is { } context && !activeNativeDownloads.ContainsKey(context.Job))
        {
            web.CoreWebView2.Stop(); nativePdfContext = null; nativePdfJobs.Remove(context.Job);
            context.Job.Status = JobStatus.Deferred;
            context.Job.DeferredReason = "VNU logout before transfer started";
            RefreshRow(context.Job);
        }
        SaveStateSnapshot();
        lblCurrent.Text = "Đăng xuất VNU: đang chờ các lượt truyền đã bắt đầu hoàn tất…";
        LogDiagnostic("logout-requested", null, new { waitingForTransfers = HasActiveTransfers });
        if (!HasActiveTransfers) await CompleteVnuLogoutAsync();
    }

    private async Task CompleteVnuLogoutAsync()
    {
        if (!logoutRequested || logoutInProgress || HasActiveTransfers) return;
        logoutInProgress = true; manualLoginMode = true;
        bool cleared = false;
        try
        {
            foreach (var popup in OwnedForms) popup.Close();
            // OpenAthens documents this sign-out endpoint. Other publisher server sessions may remain.
            var loaded = new TaskCompletionSource<bool>();
            ulong id = 0;
            void Started(object? s, CoreWebView2NavigationStartingEventArgs e) { if (e.Uri == "https://login.openathens.net/signout") id = e.NavigationId; }
            void Finished(object? s, CoreWebView2NavigationCompletedEventArgs e) { if (id != 0 && e.NavigationId == id) loaded.TrySetResult(e.IsSuccess); }
            web.CoreWebView2.NavigationStarting += Started;
            web.CoreWebView2.NavigationCompleted += Finished;
            try
            {
                web.CoreWebView2.Navigate("https://login.openathens.net/signout");
                bool success = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(12));
                LogDiagnostic("server-signout-request", null, new { navigationSucceeded = success });
            }
            catch (Exception ex) { LogDiagnostic("server-signout-request", null, new { navigationSucceeded = false, error = ex.Message }); }
            finally { web.CoreWebView2.NavigationStarting -= Started; web.CoreWebView2.NavigationCompleted -= Finished; web.CoreWebView2.Stop(); }
            web.CoreWebView2.Navigate("about:blank");
            web.CoreWebView2.CookieManager.DeleteAllCookies();
            await web.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.ServiceWorkers).WaitAsync(TimeSpan.FromSeconds(30));
            web.CoreWebView2.CookieManager.DeleteAllCookies();
            cleared = true;
            foreach (var resource in libraryResources) SetLibraryStatus(resource, "Đã đăng xuất hồ sơ VNU; cần đăng nhập lại");
            activeLibraryResource = null; librarySample = null;
            lblCurrent.Text = "Đã xóa phiên VNU trong ứng dụng. PDF/CSV/tiến độ và hồ sơ PTIT được giữ nguyên. Phiên trên trình duyệt khác/máy chủ khác không được bảo đảm đăng xuất.";
        }
        catch (Exception ex)
        {
            profileUnavailable = true;
            lblCurrent.Text = "Chưa xác nhận xóa xong phiên VNU; đóng và mở lại ứng dụng trước khi đăng nhập/tải: " + ex.Message;
        }
        finally
        {
            logoutInProgress = false; logoutRequested = false;
            btnLogoutVnu.Enabled = !profileUnavailable;
            LogDiagnostic("logout-complete", null, new { localProfileCleared = cleared, otherProfileUntouched = true });
            SaveStateSnapshot(); SaveRunSummary(false);
        }
    }
}
