# Changelog

## Unreleased — local development branch

- Carry separate immutable schema/name identity through table selection, catalog extraction, relationships, metadata export and provider sampling. Keep ordinary keys stable and escape dot/bracket-containing names without delimiter collisions.
- Add format-2 raw identity manifests and typed FK/index/constraint/trigger identities; preserve existing table arrays and readable reference fields. Reject ambiguous legacy selections instead of guessing.
- Verify separate selection of real tempdb delimiter collisions, same-named composite FK/index groups, constraints/triggers/views/routines, sampling and an expected failing ambiguous-key mutation. Keep live PostgreSQL/MySQL and updated-package native checks explicit.
- Added a local Evidence PDF with a source-field manifest and sample-free schema JSON; no model request is required. Export names are unique even within one second.
- Fixed clipped relationship-map columns and supplied the Windows window title. Verified the offline demo, native JSON export and four-page PDF on Windows 11.

- Replaced hand-built connection strings and permissive TLS defaults with provider builders and an explicit local certificate exception.
- Made every remote model run show its recipient and metadata scope; excluded sample rows from all model requests and removed the Gemini key from the URL.
- Replaced overclaiming analysis prompts with evidence-limited schema topics, source paths, hypothesis labels and input budgeting.
- Preserved schema-qualified table identity, composite foreign-key pairs, ordered index keys, included columns, filters and available constraint definitions. Added safe, bounded optional sampling.
- Replaced CDN Mermaid with a self-contained relationship map that treats database names as data. Added a synthetic offline demo and cancelable model requests.
- Updated the MAUI interface, app identity/icon, README, support matrix, tests and portable CI workflow.

This entry describes local source changes. It does **not** announce a public release or certify a packaged app installation.
