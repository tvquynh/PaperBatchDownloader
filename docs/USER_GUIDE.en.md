# User guide — V5.3.0

## V5.3.0: access modes and downloads

Select **PTIT/IP** for your current entitled network, or **VNU — OpenAthens**, then **Đổi hồ sơ** to switch. PTIT/IP opens publishers directly and does not change your IP/VPN. VNU routes supported article pages through the library portal; sign in through **LIBRARY ACCESS** first. Complete any sign-in/CAPTCHA yourself.

Each mode has separate cookies, queue state and diagnostics. Old V5.2.11 data is retained but not imported: sign in again on first use. Do not run two windows using the same profile.

**PDF đồng thời** controls concurrent native downloads (default 2, range 1–4); **PDF/nguồn** caps each publisher (default 1, range 1–2). The browser can navigate ahead while earlier downloads transfer. Direct anonymous HTTP downloads in IP mode remain serial. Throughput depends on source and network.

**Đăng xuất VNU** pauses the queue, waits for transfers, requests OpenAthens sign-out and clears VNU browser cookies, site data, cache and service workers. It retains PDFs and queue state. Other browsers and all publisher server-side sessions are not necessarily signed out. If clearing fails, restart before continuing.

ScienceDirect error pages with **CPE00002** suspend that publisher for the current pass and defer its pending articles while other sources continue. Retry only after access returns. The browser area starts taller; drag its divider to resize. Column filters affect display only.

For comparison runs, use the same CSV, equal limits and separate empty PDF folders. Export results and open the log folder after each run. Logs include profile/mode, run ID, CSV SHA-256 and per-run summaries. Review/redact personal information before sharing. Storage is under `%LOCALAPPDATA%\PaperBatchDownloader\Profiles\PTIT_IP` or `VNU_ACCOUNT`.


## Install and start

Download `PaperBatchDownloader-v5.2.11-win-x64.zip` from [Releases](https://github.com/tvquynh/PaperBatchDownloader/releases/latest), extract the complete archive to a writable folder, and run `PaperBatchDownloader.exe`. Do not run inside the ZIP. Wait for **Browser ready**. The ZIP is self-contained for .NET; you do not need the .NET SDK to run it.

This release targets Windows x64 with Microsoft WebView2 Evergreen. Install the [WebView2 Runtime from Microsoft](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section) if the browser cannot initialize. There is no native macOS, Linux, or ARM64 package in this release. An Internet connection and writable PDF folder are required. The executable is not code-signed; obtain it from this repository's Releases and compare its archive SHA-256 with the supplied checksum file.

## First download

1. Click **CSV…**, choose `examples/quickstart_oa.csv`, then **Load CSV**.
2. Check the titles, DOI values, and Selected URL column.
3. Click **Folder…** and choose a new empty output folder.
4. Prepare library access if needed, as described below.
5. Press **START**. Keep the app open while transfers are active.
6. Inspect Completed rows, open a sample PDF, and use **Export result CSV** for the full report.

Start with a few articles. The optional `new_oa_test/NEW_OA_150_3_each.csv` covers 150 metadata-backed OA candidates from 39 journals and 11 conferences, three per venue. Its manifest states the sources and selection limits. It is a download test list, not a malware research benchmark or a promise of 150 successful downloads.

<a id="csv"></a>
## CSV

Use comma-separated UTF-8 text with a header. Quote fields containing commas, quotation marks, or newlines; double embedded quotation marks. If Excel exports a semicolon-delimited file, export it again with comma delimiters.

A simple URL list:

```csv
url
https://arxiv.org/pdf/1706.03762
```

A DOI list with fixed filenames:

```csv
doi,title,save_as
10.48550/arXiv.1706.03762,Attention Is All You Need,attention.pdf
```

| Field | Meaning |
| --- | --- |
| `doi`, or `doi_url` | DOI identifier or DOI URL. |
| `best_oa_pdf_url` | Highest-priority PDF URL. |
| `pdf_url`, or `oa_url` | Next-priority OA/PDF URL; `pdf_url` wins when both are present. |
| `best_oa_landing_page`, or `landing_page` | Article page; the former wins if both are present. |
| `url` | Fallback URL, or the only field for a simple list. |
| `title` | Display/search title. |
| `save_as` | Optional unique `.pdf` filename without any directory path. |
| `publisher`, `source` | Publisher and journal/source metadata used for grouping. |
| `is_open_access`, `oa_status` | Descriptive metadata; does not confer access. |
| `record_id`, `openalex_id`, `publication_year` | Optional metadata retained for reporting. |

URL priority: **best_oa_pdf_url → pdf_url/oa_url → best_oa_landing_page/landing_page → DOI → url**. A failure at a higher-priority URL does not imply that every other input column will automatically be tried. Correct the CSV if the selected route is unsuitable.

Explicit `save_as` filenames are preserved, not silently renamed or overwritten. Use a separate empty folder for a new run. If omitted, the app derives a filename from the download response or a fallback. Avoid duplicate article records unless intentionally testing routes with different filenames.

## Sign in before running

Load the CSV, then click **LIBRARY ACCESS**. Select a resource and click **Đăng nhập nguồn** (sign in to source). Complete authentication, MFA and CAPTCHA yourself in the browser below. SSO popups use the same WebView profile. **Nguồn tiếp theo** opens the next source; you do not have to prepare every source before starting.

The 14 built-in entry routes were configured for VNU library access: arXiv, BNEUF, IEEE Xplore, MathSciNet, MIT OpenCourseWare, Nature, Open Dissertations, Ovid, Oxford Scholarship Online, ProQuest, ScienceDirect, Scopus, SpringerLink and SpringerOpen. Catalogue/learning resources are not necessarily article-PDF providers. Additional CSV publishers, such as ACM, appear with direct URLs and a note when no VNU portal has been configured.

**For other institutions:** select a source, replace **URL đăng nhập** with your institution's library entry URL, and press **Lưu URL** (save URL). The bundled VNU routes do not provide you with a VNU account or subscription. Sign-in in a separate Chrome/Edge browser does not automatically share cookies with this application. An active OpenAthens session may take you directly to the publisher; otherwise the institutional sign-in screen is an expected step. Sessions can expire.

**Mở bài mẫu** opens a sample CSV article, preferring one not marked OA if available. After it loads, **Kiểm tra trang** checks its page. Results distinguish login, CAPTCHA, purchase/subscription notices, an unverified PDF link, and a page explicitly confirming access to the matching sample. None proves access to all titles or a completed PDF transfer. Sources with no CSV article have no sample to check.

Drag the splitter between the table and browser to give the sign-in page more room. Wait for active PDF transfer/setup to finish before switching to a login source. Access preparation pauses the queue; afterward use **START** for Pending rows or **RETRY INCOMPLETE** for deferred work.

## Controls and queue behavior

| Control | Purpose |
| --- | --- |
| START | Run Pending rows; does not rerun Completed rows. |
| PAUSE | Stop advancing the queue; a native transfer already in progress can continue. |
| CONTINUE CURRENT | Reanalyze the current article after handling its page during queue processing. After LIBRARY ACCESS, use START/RETRY INCOMPLETE instead. |
| SKIP | Skip the current article manually. |
| RETRY INCOMPLETE | Retry eligible unfinished rows, retaining completed work and avoiding duplicate active background transfers. |
| RETRY PUBLISHER | Retry unfinished rows for the selected row's publisher. |
| OPEN PUBLISHER LOGIN | Open the selected publisher's configured library route, or its direct entry URL. |
| Export result CSV | Export the whole queue, including rows hidden by filters. |
| Preflight/publisher | Number of initial samples per publisher to schedule first, not concurrent-download count or access verification. |
| Wait timeout (s) | Existing attention/idle wait limit, 15–600 seconds; native progress extends the idle deadline. |
| OPEN LOG FOLDER | Open the current session diagnostics folder. |
| SAVE WEBVIEW SNAPSHOT | Save visible page evidence; pause first when capturing a specific failure. |

The queue processes initial publisher samples and interleaves the remaining work. A login/CAPTCHA timeout can suspend the remaining rows of that publisher until you resolve authentication and retry.

## Status and filtering

Pending waits for START. Navigating/LookingForPdf opens a page or discovers a candidate; Downloading represents PDF setup/transfer. Completed means the file passed the app's current checks, not a semantic review of every page. LoginRequired, InstitutionalAccessRequired, BotCheck, WaitingForUser and ManualAttention require inspecting the embedded page. Deferred retains the reason for later retry. Failed/NoUrl identifies an error or missing usable URL; Skipped records an intentional skip or non-paper URL such as robots.txt.

Explicit purchase/subscription or institutional non-subscription notices become **Deferred — Paywall** immediately, without waiting for timeout. Only that article is deferred. Login/CAPTCHA and ambiguous access-denied pages retain separate handling. Retry after your access changes; the app does not bypass restrictions.

Click a column header **▾** to filter. Text search ignores case; Status, Publisher and URL source also offer exact choices. Multiple columns combine. **Clear this column** removes one filter; **Clear all filters** restores the full view. Filters affect display only, not the queue or full export. Right-click a cell to copy DOI, URLs, title, file path or error/decision. The browser menu includes **Copy current page URL**.

## Resume and upgrade

Close the old app after active transfers finish, extract the new version separately, and run the new executable. LocalAppData profile storage means moving the executable does not deliberately erase sessions.

To resume the most recently saved queue, retain the CSV's **exact absolute path**, records/order, and PDF folder. Load that CSV, select the same output folder, and press RETRY INCOMPLETE. A copied CSV at another path is a different queue. V5.3.0 saves the most recent queue, not independent state for unlimited projects.

For a fresh run, copy the input to a new path and use an empty output folder. Do not blindly delete existing PDFs to resolve filename conflicts. Some complete late downloads recover after identity/completeness checks. Recognized old Springer supplementary/reference PDFs are deferred; retrying into the same folder preserves those files separately in Diagnostics before downloading the main article.

## Troubleshooting

| Problem | Action |
| --- | --- |
| Browser will not initialize | Install/update Microsoft WebView2 Evergreen and restart the app. |
| No URL / all CSV data in one column | Check supported headers, comma delimiter and UTF-8. |
| File already exists | Verify/move the old file or choose a new folder; check duplicate `save_as`. |
| Signed in but article still costs money | Personal sign-in may not enable institutional rights; check the library route, title coverage and article-specific access. |
| Publisher rows suspended | Finish authentication and use RETRY PUBLISHER. |
| Invalid PDF / HTML response | Inspect the login/challenge page and retry after resolving access. |
| Slow transfer or late completion | Check network/size/log progress; avoid repeatedly retrying an active transfer. |
| OA metadata but Deferred | OA metadata can be stale, incorrect, or refer to a repository copy; inspect the actual page and reason. |

When reporting a problem, provide version, Windows/WebView2 details, a public DOI/URL, expected/actual behavior, and redacted diagnostic evidence. Read [PRIVACY.md](PRIVACY.md) before sharing files. Never publish credentials or an entire browser profile. Publisher pages and SSO flows change; there is no guarantee for every source. Respect access rights and the relevant usage conditions.
