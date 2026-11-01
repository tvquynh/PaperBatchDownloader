# Paper Batch Downloader

**V5.3.3 · Windows x64 · C# / .NET 8 / WebView2 · MIT**

Download research-paper PDFs from a CSV using an embedded browser, your existing publisher/library access, and a resumable queue. Complete institutional sign-in and CAPTCHA yourself in the application. Includes a library-access workspace, column filters, per-article decisions, progress recovery, and local diagnostics.

**[Download the Windows release](https://github.com/tvquynh/PaperBatchDownloader/releases/latest)** · **[English user guide](docs/USER_GUIDE.en.md)**

## Quick start

1. Download the **English documentation** Windows ZIP from [Releases](https://github.com/tvquynh/PaperBatchDownloader/releases/latest) and extract it to a writable folder.
2. Open `PaperBatchDownloader-v5.3.3.exe`, or use `OPEN_PTIT.cmd` / `OPEN_VNU.cmd` to select an access mode.
3. Choose a PDF output folder, select `examples/quickstart_oa.csv` with **CSV...**, and click **Load CSV**.
4. For institutional access, select the appropriate mode and prepare sign-in through **LIBRARY ACCESS**. Complete sign-in and CAPTCHA yourself.
5. Optionally set the **rest between papers (minutes)** control: **3** waits three minutes after each completed PDF; **0** keeps continuous downloads. Press **START**. Inspect downloaded PDFs and use **Export result CSV** to save the results.
6. To download unfinished articles through the other access mode, wait for active transfers, switch profiles, keep the same CSV/output folder, and press **START** again. Completed articles are retained.

Requires Windows x64 and [Microsoft WebView2 Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section). The ZIP includes .NET; users do not need the .NET SDK or an API key to run it. There is no native macOS, Linux or ARM64 package in this release.

Some Windows controls are localized. The [user guide](docs/USER_GUIDE.en.md) describes their functions in English.

## Features

- CSV input: DOI, article landing page, or direct PDF; optional fixed output filenames.
- Shared queue progress across PTIT/IP and VNU for the same CSV and output folder; switch access modes to download only unfinished papers.
- Separate browser cookies and sign-in sessions, with VNU logout and profile-specific diagnostics.
- Configurable rest between completed papers, with a countdown, pause/resume and saved per-profile settings. A positive interval downloads papers one at a time; zero retains normal concurrency.
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

For your own data, use `examples/template.csv` as a starting point. Supported formats and URL priority are documented in [the user guide](docs/USER_GUIDE.en.md#csv).

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
