# Changelog

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
