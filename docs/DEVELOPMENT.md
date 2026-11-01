# Build, test and package

Prerequisites: Windows x64, [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), Microsoft WebView2 Evergreen, and network access for NuGet restore. Application entry point: `PaperBatchDownloader/Program.cs`; UI and browser logic are in the `MainForm` partial files. The NuGet dependency is `Microsoft.Web.WebView2` 1.0.3124.44.

```powershell
dotnet restore PaperBatchDownloader.sln
dotnet build PaperBatchDownloader.sln -c Release
dotnet run --project tests/IeeeRegression.csproj -c Release
```

Run from the repository root. Fixture tests create Windows forms and WebView controls; use an interactive Windows session. They use temporary browser profiles and simulated publisher HTML/PDF responses, not personal library credentials. The suite covers CSV/route handling, IEEE stamp/iframe downloads, ScienceDirect PII/redirect selection, invalid HTML-as-PDF, queue progression, native interruption/recovery, late completion, Springer supplementary preservation, filters, immediate paywall deferral, and library/SSO behavior.

Targeted options:

```powershell
dotnet run --project tests/IeeeRegression.csproj -c Release -- --library-only
dotnet run --project tests/IeeeRegression.csproj -c Release -- --filters-only
dotnet run --project tests/IeeeRegression.csproj -c Release -- --paywall-only
dotnet run --project tests/IeeeRegression.csproj -c Release -- --validate-new-oa
```

`--validate-new-oa` validates the included 150-row CSV using the app's parser; it does not download it. Other `--live-*`/`--inspect-*` options in the development harness intentionally use the normal local browser profile for explicit live inspection, while storing queue state separately. They make real publisher requests: read `tests/InspectScienceDirect.cs` and understand the selected URLs before invoking them. They are not part of the default fixture suite.

Publish:

```powershell
./scripts/build_release.ps1
```

This produces a self-contained Windows x64 executable in `publish-win-x64/`. The existing solution also builds through Visual Studio with .NET desktop development support. Building does not install the separate Evergreen WebView2 Runtime. NuGet restore obtains dependencies; no API key is required.

The known WindowsBase/WebView2 WPF reference-resolution warning and unused retry-mode-field warning may appear. They were warnings, not test failures, in the validated V5.2.11 build. Investigate new errors rather than treating every warning as equivalent.

For public distribution, package the executable, user guides, examples, MIT license and third-party notices. Exclude PDBs, browser profiles, diagnostics, queue state, downloaded PDFs and private CSVs. Include a SHA-256 checksum for the release archive. Do not put the self-contained executable in Git history; use Release assets. Metadata test packs contain links and bibliographic records, not the articles themselves.

## Validation scope

The maintainer reported V5.2.11 working well in their local use. Automated fixtures are reproducible checks for specific responses, not a guarantee for all current publisher sites, institutional accounts, content licenses, or Windows configurations. Neither the source distribution nor its tests include the maintainer's account or real session logs.

## V5.3.0 checks

Additional fixture options: `--profiles-only`, `--pipeline-only`, `--publisher-block-only`. They exercise profile isolation/logout, concurrency ownership and limits, response cooldowns and HTTP-200 CPE error pages. The full fixture suite passed for the download changes; filter/UI checks passed after the final cosmetic cleanup. Institutional access and real throughput require live runs.

`python scripts/compare_access_runs.py --help` documents comparing exported run diagnostics.

## V5.3.2 shared queue

Production progress is shared across access modes and keyed by normalized CSV path, CSV SHA-256 and output folder under `SharedQueues/<batch>`. Browser sessions and diagnostics remain under `Profiles/<mode>`. Temporary test profiles default to isolated queue storage; shared-queue fixtures explicitly supply a common temporary queue root.

Run `dotnet run --project tests/IeeeRegression.csproj -c Release -- --shared-queue-only` for complementary PTIT/VNU downloads, profile switching, missing PDF recovery, legacy state migration and changed CSV/folder isolation. The full regression suite passed for the queue change; final focused checks also cover the migration safeguards and per-run existing/new completion counts. A successful live download was reported by the maintainer; an earlier suspected network interruption was not independently diagnosed.
