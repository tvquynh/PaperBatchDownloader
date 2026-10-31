# Contributing

Use GitHub Issues for reproducible behavior reports and Pull Requests for focused changes. Include the version, Windows/WebView2 environment, expected/actual result, and a public DOI/URL or a minimal synthetic CSV. Review diagnostics as described in [PRIVACY.md](docs/PRIVACY.md) before posting. Do not upload passwords, browser profiles, private queue state, downloaded copyrighted papers, or session-bearing URLs.

For a publisher change, prefer a local HTML/PDF fixture that demonstrates the incorrect article selection, authentication state, or queue transition. Run the relevant fixture and the full Windows regression suite when shared download/queue behavior changes. State exactly what was verified; distinguish fixtures from live publisher checks.

Use an isolated test profile instead of altering a user's normal WebView profile. Preserve queue identity and fixed filenames. Do not add automatic CAPTCHA solving, subscription bypasses, credential collection, or a claim that one successful OA download verifies institutional access.

By submitting changes, you agree that your contribution is provided under the repository's MIT license. Third-party components retain their own licenses.
