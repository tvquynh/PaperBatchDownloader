# Paper Batch Downloader

**V5.3.0 · Windows x64 · C# / .NET 8 / WebView2 · MIT**

Download research-paper PDFs from a CSV using an embedded browser, your existing publisher/library access, and a resumable queue. Complete institutional sign-in and CAPTCHA yourself in the application. Includes a library-access workspace, column filters, per-article decisions, progress recovery, and local diagnostics.

**[Download the Windows release](https://github.com/tvquynh/PaperBatchDownloader/releases/latest)** · **[Hướng dẫn tiếng Việt](docs/USER_GUIDE.vi.md)** · **[English user guide](docs/USER_GUIDE.en.md)**

## Bắt đầu nhanh

1. Trong **Releases**, tải `PaperBatchDownloader-v5.3.0-win-x64.zip`; giải nén vào thư mục có quyền ghi.
2. Mở `PaperBatchDownloader.exe`. Nếu thiếu WebView2, cài [Microsoft WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section).
3. Bấm **CSV…**, chọn `examples/quickstart_oa.csv`, rồi **Load CSV**.
4. Chọn thư mục kết quả mới bằng **Folder…**.
5. Chọn PTIT/IP hoặc VNU và bấm **Đổi hồ sơ**. Với VNU: **LIBRARY ACCESS → Đăng nhập nguồn → Mở bài mẫu → Kiểm tra trang**.
6. Bấm **START**. Sau khi xong, dùng **Export result CSV** để lưu báo cáo.

Bản ZIP chứa .NET runtime; người dùng không cần cài .NET SDK để chạy. Ứng dụng dành cho Windows x64 với WebView2 Evergreen; không có bản macOS/Linux hoặc ARM64 native trong release này. Không cần API key.

## Quick start

Download and extract the Windows ZIP from Releases, run `PaperBatchDownloader.exe`, load `examples/quickstart_oa.csv`, choose a new output folder, and press **START**. If your articles need institutional access, prepare that session in **LIBRARY ACCESS** before starting. See the guides for resuming, CSV fields, and troubleshooting.

## Features

- CSV input: DOI, article landing page, or direct PDF; optional fixed output filenames.
- Separate PTIT/IP and VNU/OpenAthens profiles, with VNU logout and profile-specific diagnostics.
- Overlapping native PDF downloads with global/per-publisher caps; ScienceDirect CPE block recognition.
- Larger default browser area and a simplified interface.
- Fourteen VNU library entry routes, editable in VNU mode; additional publishers from the CSV appear with direct entry URLs. Users at other institutions can edit the URLs.
- Per-publisher preflight samples, followed by interleaved queue processing.
- Explicit article paywalls defer immediately; login/CAPTCHA pages retain manual-attention handling.
- Article-specific ScienceDirect PII and Springer DOI matching to avoid reference/supplementary PDFs.
- Late-download recovery, saved progress, retries, full result export, and local diagnostic snapshots.
- Combine filters from column headers; right-click cells to copy DOI, URLs, title, path, or error.

## CSV example

```csv
doi,title,save_as
10.48550/arXiv.1706.03762,Attention Is All You Need,attention.pdf
```

For your own data, use `examples/template.csv` as a starting point. Supported formats and URL priority are documented in [the Vietnamese guide](docs/USER_GUIDE.vi.md#csv) and [the English guide](docs/USER_GUIDE.en.md#csv).

## Test inputs and validation

- `examples/quickstart_oa.csv`: three small starting cases across arXiv, Springer, and ScienceDirect. Availability and session requirements can change.
- `new_oa_test/NEW_OA_150_3_each.csv`: 150 metadata-backed OA candidates from 39 journals and 11 conference series, exactly three papers per venue. See its manifest for sources and scope. These are downloader test inputs, not a malware research benchmark or proof of 150 successful downloads.
- Optional release asset `journal_coverage_test_pack.zip`: 23,166 metadata cases spanning 7,694 journals and 28 conference series. Elsevier includes mixed access. The original data guide describes scope and exclusions; its historical version suggestions are superseded by this README.
- Automated fixtures exercise library routes and SSO cookie sharing, CSV parsing, filters, paywall deferral, IEEE/ScienceDirect handling, queue advancement, and recovery. They do not certify every publisher or subscribed title. See [developer instructions](docs/DEVELOPMENT.md).

## Access, files, and privacy

Use sources you are entitled to access and observe your institution/publisher's usage conditions. A library catalogue entry does not guarantee full-text access to every title. The app does not solve CAPTCHA, bypass paywalls, supply library accounts, or grant subscriptions.

Browser sessions, local queue state and diagnostics live under `%LOCALAPPDATA%\PaperBatchDownloader`. The repository and release do not include a user's browser profile, credentials, downloaded papers, or diagnostic logs. Review URLs and visible text in snapshots before sharing them. Full details: [PRIVACY.md](docs/PRIVACY.md).

## Build from source

Install the [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) on Windows, then run from the repository root:

```powershell
dotnet restore PaperBatchDownloader.sln
dotnet publish PaperBatchDownloader/PaperBatchDownloader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-win-x64
```

Alternatively run `scripts/build_release.ps1`. See [development and tests](docs/DEVELOPMENT.md), [release notes](CHANGELOG.md), and [third-party notices](THIRD_PARTY_NOTICES.md).

## Report a problem

Open a GitHub issue with the app version, Windows/WebView2 version, a public DOI or URL, expected/actual behavior, and a redacted error or snapshot. Never post passwords, browser profiles, session links, or authentication codes. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE), copyright 2026 Quynh Trinh (tvquynh). Dependency notices and licenses are included separately in `third_party/`. Paper metadata and linked publications remain subject to their respective sources and licenses.
