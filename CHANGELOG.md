# Changelog

## V5.3.2 — 2026-11-01

- Share queue progress across PTIT/IP and VNU for the same CSV and PDF folder.
- Automatically load shared progress and requeue unfinished articles after switching access mode.
- Recover verified old PDF files, retain suggested filenames, and requeue missing completions.
- Separate existing completions from new downloads in run summaries.
- Keep browser profiles/cookies and diagnostic logs separate.

## V5.3.1 — 2026-11-01

- Preserve CSV/output selections when switching access profiles; executable names include the version.

## V5.3.0 — 2026-11-01

- Separate PTIT/IP and VNU profiles; VNU logout with transfer-safe browser-data clearing.
- Overlapping native downloads, concurrency limits, cooldowns and per-run comparison logs.
- Fix IEEE zero-padded IDs, CSV file_name aliases and Continue after manual sign-in.
- Recognize ScienceDirect CPE00002 and defer the blocked publisher for the current pass.
- Increase default browser height; remove development notes from the main interface.
- Fixture regression suite and UI checks pass; live institutional access/throughput remains dependent on the network and subscription.

## V5.2.11 — public release

- Library-access workspace with 14 editable VNU/OpenAthens/direct routes and per-CSV publisher counts.
- Manual institutional authentication and shared-profile SSO popups; no account is bundled.
- Sample-page checks distinguish login, CAPTCHA, paywall, unverified PDF links and explicit access confirmation for the matching article.
- Library URL overrides saved locally; access-check results are not restored as authenticated state on restart.
- Preparation preserves queue statuses and avoids switching away from an active PDF transfer/setup.
- Public packaging adds Vietnamese/English guides, examples, MIT licensing, third-party notices and privacy/reporting instructions.

## V5.2.10

- Explicit article paywalls/institutional non-subscription defer immediately and allow the next publisher article to run.
- CAPTCHA, login-only and ambiguous access-denied pages retain manual-attention handling.

## V5.2.9

- Combined column-header filters, exact categorical choices, visible/total count and clear controls.
- Filtered row selection follows article identity without changing queue order.

## V5.2.8

- Late native completion and saved complete-PDF recovery.
- Springer DOI matching excludes unrelated references and supplementary files; incorrect old files are preserved separately on retry.
- Interrupted native downloads release pending state and selected interruptions receive one browser-session fetch attempt.

## V5.2.7 and earlier

- ScienceDirect article PII matching, normalized View PDF labels and HTML `/pdfft` redirects.
- IEEE stamp/iframe PDF handling, invalid response rejection, queue recovery and local diagnostic snapshots.
- Right-click copying for DOI, URLs, title, paths and errors.
