# Local Windows validation

27 September 2026, Windows 11, .NET 9 MAUI Windows x64. These checks use the packaged synthetic demo, not a user's database. They are development evidence, not a fresh-machine installation certificate.

| Path | Observed result |
| --- | --- |
| Main window | Identifiable AI Database Analyzer title; synthetic demo available without credentials |
| Try the offline demo | Three tables and two foreign keys; no database or model required |
| Export JSON | Parsed output equals the packaged demo; three tables and no sample rows |
| Visualize | Real WebView rendered all three cards with every column and relationship; no CDN dependency |
| Evidence PDF | Native command created a four-page PDF with the source-field manifest and sample-free schema JSON |
| Independent PDF inspection | All four pages rendered and inspected; readable text, page numbers, no clipped or overlapping content |
| SQL Server tests | 22 passed, zero skipped, with `AIDB_TEST_SQLSERVER` set to `.\SQLEXPRESS` |

The prior map applied the global 34-pixel button height to table cards and clipped columns. Cards now have automatic height. The screenshots below were captured from the actual application using synthetic metadata.

![Offline demo and export actions](images/offline-demo.png)

![Every column visible in the native offline map](images/offline-map.png)

The Windows Release build passed with existing nullable, XAML and MVVM analyzer warnings (274 in this build); warnings were not disabled. The Evidence PDF identifies itself as local metadata, with no model request or sample rows. It does not assert a workload defect, permission finding or compliance result. Optional local JSON exports can contain samples when explicitly selected; the demo has none.

No Ollama service was available for a real model run. Gemini requests, PostgreSQL/MySQL servers, production certificates, full keyboard/screen-reader/scaling coverage, non-Windows builds and fresh-machine installation are separate checks. A successful local demo is not evidence for those paths.
