using System.ComponentModel;
using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private sealed class LibraryResource
    {
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public string Publisher { get; init; } = "";
        public string LoginUrl { get; set; } = "";
        public string Note { get; init; } = "";
        public int Papers { get; set; }
        public string AccessState { get; set; } = "Chưa kiểm tra";
    }
    private readonly BindingList<LibraryResource> libraryResources = new();
    private readonly DataGridView libraryGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false,
        ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false };
    private readonly TextBox txtLibraryUrl = new() { Dock = DockStyle.Fill };
    private readonly Label lblLibraryHelp = new() { AutoSize = true, Dock = DockStyle.Top,
        Text = "Đăng nhập trong trình duyệt bên dưới. Chọn nguồn → Đăng nhập → Mở bài mẫu → Kiểm tra trang. Phiên có thể hết hạn; kết quả chỉ áp dụng bài mẫu.", Padding = new Padding(3) };
    private TabControl? libraryTabs;
    private TabPage? libraryPage;
    private LibraryResource? activeLibraryResource;
    private DownloadJob? librarySample;
    private string LibrarySettingsPath => Path.Combine(browserProfilePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader"), "library_access_urls.json");
    private static string VnuAthens(string target) => "https://go.openathens.net/redirector/vnu.edu.vn?url=" + Uri.EscapeDataString(target);

    private static IEnumerable<LibraryResource> DefaultLibraryResources()
    {
        LibraryResource R(string key, string name, string url, string publisher = "", string note = "") =>
            new() { Key = key, Name = name, LoginUrl = url, Publisher = publisher, Note = note };
        return new[] {
            R("arxiv", "arXiv", "https://m-auth.vnulib.edu.vn/readbook?key=arxiv", "arXiv", "Kho mở"),
            R("bneuf", "BNEUF", "https://bneuf.auf.org/#!/resource/home"),
            R("ieee", "IEEE Xplore", VnuAthens("http://ieeexplore.ieee.org/Xplore/home.jsp"), "IEEE", "VNU OpenAthens"),
            R("mathscinet", "MathSciNet", VnuAthens("https://mathscinet.ams.org"), note: "Tra cứu"),
            R("mit", "MIT OpenCourseWare", "https://ocw.mit.edu/index.htm", note: "Học liệu mở"),
            R("nature", "Nature", VnuAthens("https://www.nature.com"), "Nature", "VNU OpenAthens"),
            R("dissertations", "Open Dissertations", "https://m-auth.vnulib.edu.vn/readbook?key=opendissertations"),
            R("ovid", "Ovid", VnuAthens("https://ovidsp.ovid.com/ovidweb.cgi?T=JS&NEWS=n&CSC=Y&PAGE=main&D=yrovft"), "Ovid", "VNU OpenAthens"),
            R("oxford", "Oxford Scholarship Online", VnuAthens("https://academic.oup.com/books/search-results?f_ContentType=Book"), "Oxford", "Sách"),
            R("proquest", "ProQuest", VnuAthens("https://search.proquest.com/index"), "ProQuest", "VNU OpenAthens"),
            R("elsevier", "ScienceDirect", VnuAthens("https://www.sciencedirect.com"), "Elsevier", "VNU OpenAthens"),
            R("scopus", "Scopus", VnuAthens("https://www.scopus.com/"), note: "Tra cứu"),
            R("springer", "SpringerLink", VnuAthens("https://link.springer.com/"), "Springer", "VNU OpenAthens"),
            R("springeropen", "SpringerOpen", "https://www.springeropen.com/", note: "OA")
        };
    }

    private void InitializeLibraryAccess(TabControl tabs, FlowLayoutPanel buttons)
    {
        libraryTabs = tabs;
        libraryPage = new TabPage("Truy cập thư viện");
        tabs.TabPages.Add(libraryPage);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        void Button(string label, Action action)
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) => action(); actions.Controls.Add(button);
        }
        Button("Đăng nhập nguồn", () => { if (SelectedLibraryResource is { } r) OpenLibraryResource(r); });
        Button("Nguồn tiếp theo", OpenNextLibraryResource);
        Button("Mở bài mẫu", OpenLibrarySample);
        var check = new Button { Text = "Kiểm tra trang", AutoSize = true };
        check.Click += async (_, _) => { check.Enabled = false; try { await CheckLibraryPageAsync(); } finally { check.Enabled = true; } };
        actions.Controls.Add(check);
        Button("Quay lại", () => { if (manualLoginMode && web.CoreWebView2?.CanGoBack == true) web.CoreWebView2.GoBack(); });
        Button("Tải lại trang", () => { if (manualLoginMode) web.CoreWebView2?.Reload(); });
        var urlBar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        urlBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        urlBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        urlBar.Controls.Add(new Label { Text = "URL đăng nhập:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        urlBar.Controls.Add(txtLibraryUrl, 1, 0);
        var save = new Button { Text = "Lưu URL", AutoSize = true };
        save.Click += (_, _) => SaveSelectedLibraryUrl(); urlBar.Controls.Add(save, 2, 0);
        libraryGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Nguồn", Width = 215 });
        libraryGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Papers", HeaderText = "Bài CSV", Width = 80 });
        libraryGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "AccessState", HeaderText = "Kiểm tra phiên hiện tại", Width = 350 });
        libraryGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Note", HeaderText = "Ghi chú", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn col in libraryGrid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
        libraryGrid.DataSource = libraryResources;
        libraryGrid.SelectionChanged += (_, _) => { if (SelectedLibraryResource is { } r) txtLibraryUrl.Text = r.LoginUrl; };
        libraryGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && SelectedLibraryResource is { } r) OpenLibraryResource(r); };
        libraryPage.Controls.Add(libraryGrid);
        libraryPage.Controls.Add(urlBar);
        libraryPage.Controls.Add(actions);
        libraryPage.Controls.Add(lblLibraryHelp);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == libraryPage) RefreshLibraryResources(); };
        var prepare = new Button { Text = "LIBRARY ACCESS", AutoSize = true };
        prepare.Click += (_, _) => { tabs.SelectedTab = libraryPage; RefreshLibraryResources(); };
        buttons.Controls.Add(prepare);
        foreach (var resource in DefaultLibraryResources()) libraryResources.Add(resource);
        LoadLibraryUrlOverrides();
    }

    private LibraryResource? SelectedLibraryResource => libraryGrid.CurrentRow?.DataBoundItem as LibraryResource;
    private static bool MatchesLibraryResource(LibraryResource resource, DownloadJob job) =>
        resource.Key == "springeropen" ? Uri.TryCreate(job.Url, UriKind.Absolute, out var u) && (u.Host == "springeropen.com" || u.Host.EndsWith(".springeropen.com"))
        : resource.Publisher.Length > 0 && resource.Publisher.Equals(job.PublisherKey, StringComparison.OrdinalIgnoreCase)
            && !(resource.Key == "springer" && Uri.TryCreate(job.Url, UriKind.Absolute, out var s) && (s.Host == "springeropen.com" || s.Host.EndsWith(".springeropen.com")));

    private void RefreshLibraryResources()
    {
        foreach (string publisher in jobs.Select(j => j.PublisherKey).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (jobs.Where(j => j.PublisherKey == publisher).All(j => libraryResources.Any(r => MatchesLibraryResource(r, j)))) continue;
            string url = PublisherResolver.LoginUrl(publisher);
            if (url.Length == 0) url = jobs.First(j => j.PublisherKey == publisher).Url;
            libraryResources.Add(new LibraryResource { Key = "extra:" + publisher, Name = publisher, Publisher = publisher,
                LoginUrl = url, Note = "Nguồn CSV; URL trực tiếp, chưa có cổng VNU" });
        }
        LoadLibraryUrlOverrides();
        foreach (var r in libraryResources) r.Papers = jobs.Count(j => MatchesLibraryResource(r, j));
        libraryResources.ResetBindings();
        if (SelectedLibraryResource is { } selected) txtLibraryUrl.Text = selected.LoginUrl;
    }

    private void LoadLibraryUrlOverrides()
    {
        try
        {
            if (!File.Exists(LibrarySettingsPath)) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(LibrarySettingsPath));
            foreach (var r in libraryResources)
                if (saved?.TryGetValue(r.Key, out string? value) == true && IsLibraryUrl(value)) r.LoginUrl = value;
        }
        catch { /* A bad optional URL file must not prevent using built-in routes. */ }
    }
    private static bool IsLibraryUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
    private void SaveSelectedLibraryUrl()
    {
        if (SelectedLibraryResource is not { } r) return;
        string value = txtLibraryUrl.Text.Trim();
        if (!IsLibraryUrl(value)) { MessageBox.Show("URL phải bắt đầu bằng https:// hoặc http://.", "URL thư viện"); return; }
        try
        {
            r.LoginUrl = value;
            Directory.CreateDirectory(Path.GetDirectoryName(LibrarySettingsPath)!);
            File.WriteAllText(LibrarySettingsPath, JsonSerializer.Serialize(libraryResources.ToDictionary(x => x.Key, x => x.LoginUrl), new JsonSerializerOptions { WriteIndented = true }));
            SetLibraryStatus(r, "Đã lưu URL; chưa kiểm tra");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Không lưu được URL"); }
    }

    private void SetLibraryStatus(LibraryResource r, string text)
    {
        r.AccessState = text;
        int index = libraryResources.IndexOf(r);
        if (index >= 0) libraryResources.ResetItem(index);
    }
    private void OpenLibraryResource(LibraryResource resource)
    {
        if (!initialized) { lblCurrent.Text = "Chờ trình duyệt khởi tạo."; return; }
        // Keep an existing PDF transfer intact; changing its page could cancel it.
        if (nativePdfContext is not null || browserPdfFetchJob is not null || activeNativeDownloads.Values.Any(d => d.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress))
        { MessageBox.Show("Hãy chờ lượt tải PDF hiện tại hoàn tất trước khi đăng nhập nguồn.", "Đang tải PDF"); return; }
        PauseQueue();
        manualLoginMode = true;
        activeLibraryResource = resource; librarySample = null;
        if (libraryTabs is not null) libraryTabs.SelectedTab = libraryPage;
        var row = libraryGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x => ReferenceEquals(x.DataBoundItem, resource));
        if (row is not null) libraryGrid.CurrentCell = row.Cells[0];
        SetLibraryStatus(resource, "Đang mở cổng; chưa kiểm tra đăng nhập");
        txtLibraryUrl.Text = resource.LoginUrl;
        web.CoreWebView2.Navigate(resource.LoginUrl);
        lblCurrent.Text = $"{resource.Name}: đăng nhập trong trình duyệt bên dưới, sau đó mở bài mẫu và kiểm tra trang.";
    }
    private void OpenNextLibraryResource()
    {
        RefreshLibraryResources();
        int index = SelectedLibraryResource is { } r ? libraryResources.IndexOf(r) : -1;
        OpenLibraryResource(libraryResources[(index + 1) % libraryResources.Count]);
    }
    private void OpenLibrarySample()
    {
        if (!initialized || SelectedLibraryResource is not { } r) return;
        var sample = jobs.Where(j => MatchesLibraryResource(r, j)).OrderBy(j => j.IsOpenAccess.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0).FirstOrDefault();
        if (sample is null) { lblCurrent.Text = $"{r.Name}: CSV chưa có bài mẫu; bạn vẫn có thể đăng nhập nguồn này."; return; }
        OpenLibraryResourceForSample(r, sample);
    }
    private void OpenLibraryResourceForSample(LibraryResource r, DownloadJob sample)
    {
        if (nativePdfContext is not null || browserPdfFetchJob is not null || activeNativeDownloads.Values.Any(d => d.State == Microsoft.Web.WebView2.Core.CoreWebView2DownloadState.InProgress)) return;
        PauseQueue(); manualLoginMode = true; activeLibraryResource = r; librarySample = sample;
        string target = sample.Doi.Length > 0 ? "https://doi.org/" + sample.Doi : sample.Url;
        SetLibraryStatus(r, "Đã mở bài mẫu; cần kiểm tra trang");
        web.CoreWebView2.Navigate(target);
        lblCurrent.Text = $"Bài mẫu {r.Name}: {sample.Title}. Đây là kiểm tra thủ công, không thay đổi trạng thái hàng đợi.";
    }

    private async Task CheckLibraryPageAsync()
    {
        var resource = SelectedLibraryResource;
        if (!initialized || resource is null) return;
        if (!manualLoginMode || activeLibraryResource != resource)
        { SetLibraryStatus(resource, "Hãy mở đúng nguồn này trước khi kiểm tra"); return; }
        var sample = librarySample;
        string source = web.CoreWebView2.Source;
        var page = await AnalyzeCurrentPageAsync();
        if (!manualLoginMode || activeLibraryResource != resource || librarySample != sample || web.CoreWebView2.Source != source) return;
        string state = page.Classification switch {
            "BotCheck" => "Cần xử lý CAPTCHA",
            "LoginRequired" or "InstitutionalAccessRequired" => "Cần đăng nhập",
            "Paywall" or "EntitlementDenied" => sample is null ? "Trang báo yêu cầu quyền truy cập" : "Bài mẫu yêu cầu mua/thuê bao",
            "AccessRequired" => "Cần kiểm tra quyền truy cập",
            _ => "Chưa xác nhận; hãy mở bài mẫu"
        };
        if (sample is not null && page.Classification is "Normal" or "PdfCandidateFound" or "PdfInterstitial")
        {
            string raw = await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({doi:(document.querySelector('meta[name=\"citation_doi\"]')||{}).content||''})");
            string json = JsonSerializer.Deserialize<string>(raw) ?? "{}";
            using var meta = JsonDocument.Parse(json);
            string doi = meta.RootElement.GetProperty("doi").GetString() ?? "";
            bool identity = sample.Doi.Length > 0 ? doi.Trim().Equals(sample.Doi, StringComparison.OrdinalIgnoreCase)
                || page.CurrentUrl.Contains(sample.Doi, StringComparison.OrdinalIgnoreCase) : page.CurrentUrl == sample.Url;
            string text = page.PageText.ToLowerInvariant();
            if (identity && (text.Contains("you have full access to this") || text.Contains("you have access to this article")))
                state = "Trang xác nhận quyền bài mẫu; chưa tải thử PDF";
            else if (identity && page.PdfCandidates.Count > 0) state = "Bài mẫu có liên kết PDF; chưa xác minh tải";
        }
        if (!manualLoginMode || activeLibraryResource != resource || librarySample != sample || web.CoreWebView2.Source != source) return;
        SetLibraryStatus(resource, state);
        lblCurrent.Text = resource.Name + ": " + state;
    }

    private void InitializeLibraryPopupHandling()
    {
        web.CoreWebView2.NewWindowRequested += async (_, e) =>
        {
            if (!manualLoginMode) return;
            using var deferral = e.GetDeferral();
            e.Handled = true;
            Form? popup = null;
            try
            {
                popup = new Form { Text = "Đăng nhập thư viện", Width = 1050, Height = 780, StartPosition = FormStartPosition.CenterParent };
                var browser = new WebView2 { Dock = DockStyle.Fill };
                popup.Controls.Add(browser);
                popup.Show(this);
                await browser.EnsureCoreWebView2Async(web.CoreWebView2.Environment);
                browser.CoreWebView2.WindowCloseRequested += (_, _) => popup.Close();
                e.NewWindow = browser.CoreWebView2;
            }
            catch (Exception ex) { popup?.Close(); lblCurrent.Text = "Không mở được cửa sổ đăng nhập: " + ex.Message; }
        };
    }
}
