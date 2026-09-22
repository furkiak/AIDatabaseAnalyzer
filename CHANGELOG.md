# Changelog

## Unreleased — local development branch

- Replaced hand-built connection strings and permissive TLS defaults with provider builders and an explicit local certificate exception.
- Made every remote model run show its recipient and metadata scope; excluded sample rows from all model requests and removed the Gemini key from the URL.
- Replaced overclaiming analysis prompts with evidence-limited schema topics, source paths, hypothesis labels and input budgeting.
- Preserved schema-qualified table identity, composite foreign-key pairs, ordered index keys, included columns, filters and available constraint definitions. Added safe, bounded optional sampling.
- Replaced CDN Mermaid with a self-contained relationship map that treats database names as data. Added a synthetic offline demo and cancelable model requests.
- Updated the MAUI interface, app identity/icon, README, support matrix, tests and portable CI workflow.

This entry describes local source changes. It does **not** announce a public release or certify a packaged app installation.
