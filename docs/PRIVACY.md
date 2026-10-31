# Local data and diagnostics

The app has no application analytics or diagnostic-upload service. It connects to DOI services, publishers, library identity providers and article resources as needed. Web pages may make their own third-party requests. Authentication is entered by the user in WebView2; the app does not provide an account, collect an app-managed plaintext password list, or grant access.

Default local storage on Windows:

| Location under `%LOCALAPPDATA%\PaperBatchDownloader` | Contents |
| --- | --- |
| `WebView2ProfileV51\` | Browser-managed cookies, storage, cache and session information. Treat the entire directory as private. |
| `state_v51.json` | Latest queue's CSV path, article identifiers, statuses, errors, file/PDF URLs and attempts. |
| `last_results.csv` | Automatically saved latest queue report. |
| `library_access_urls.json` | Customized library entry URLs. Avoid saving temporary authenticated/session URLs here. |
| `Diagnostics\<session>\events.jsonl` | Navigation, relevant HTTP/download events, state changes and page-analysis evidence. |
| `Diagnostics\<session>\webview-*.json` | Manually requested page snapshots and analysis, when saved. |

PDFs are written to the output folder you select. Retry recovery can preserve a recognized non-main Springer PDF in the session diagnostics directory. Test harnesses using an explicitly supplied profile keep their state/diagnostics separately within that profile.

Diagnostics do not deliberately capture password input values, cookies or request headers. **URLs, visible page text, account labels, file paths and authentication redirect parameters can nevertheless be sensitive.** This is why no real user diagnostics/profile is bundled with the public source or release.

Before posting an issue, inspect the exact excerpt or snapshot you intend to share. Remove account identifiers, institution-private material, authentication/session tokens, signed download parameters, private titles and local personal paths. Prefer a public DOI and the smallest relevant error excerpt. Do not attach the full WebView profile or queue-state directory.

Closing/reopening the app does not guarantee a session remains valid: expiry is controlled by the source. If you intentionally want to reset local app data, first close the app after transfers finish and back up the selected PDF folder and any needed queue files. Renaming the local app-data folder is a reversible way to start a new profile; it also disconnects the app from previous saved progress and sessions.
