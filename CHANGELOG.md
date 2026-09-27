# Changelog

## Unreleased — local development branch

- Compile bindings for three page and four item contexts, convert 44 observable fields to C# partial properties without changing public member names/defaults, create the root Shell through `CreateWindow`, and require report-section text. Replace obsolete main-page access with the current Shell. The local Windows build drops from 277 warnings to zero; warnings now fail the build rather than being suppressed. Reject null/empty/whitespace/non-text Ollama/Gemini answers; eight production-service handler regressions failed before the fix, and all 86 tests now pass. NativeAOT and actual new-runtime UI/export acceptance are separate gates.
- Move all declared targets, tests and the offline map helper to .NET 10, pin MAUI Controls 10.0.101 and align HTTP/logging/DPAPI packages to 10.0.12. Keep metadata/profile formats unchanged. Stop restores on known direct/transitive NuGet vulnerabilities and add a Windows app-build CI job alongside portable tests. Local Windows build and 78 tests passed; new-runtime native/package/installation and remote CI remain separate gates. See `docs/RUNTIME.md` for the distinct MAUI and runtime support schedules.
- Give all app-owned Windows notifications and confirmations explicit saved theme/font, wrapping button labels and bounded message scrolling, retaining the existing primary/secondary/None-default result contract and non-Windows DisplayAlert behavior. Apply active/inactive app title-bar colors without changing OS settings. Follow-up native dialog/title-bar acceptance is pending; the prior native Picker/tab/400×400 button correction was exercised locally.
- Synchronize saved Light/Dark with the Windows content/navigation roots at startup and after appearance save, scale app-owned Shell labels with the text preference, and constrain existing native button content before wrapping measurement. Preserve OS theme, native focus/selection/commands and profile/model state. These fixes follow real cb27226 Picker/tab/button findings; corrected-binary native acceptance remains pending. Repair the offline demo caption's Unicode separator.
- Reserve the whole normal unpackaged Windows package container (Data/Settings/Cache) against isolated profiles, including empty or previously marked sibling directories. Preserve sentinel/digest bytes and keep publisher sibling applications usable; the old Data-only mutation fails six tests.
- Add independent persistent MAUI Light/Dark and 100/150/200% actual text size, opaque accessible-color tokens, explicit labels and 400-pixel single-column/wrapping/scrolling layouts. Appearance-only saves preserve model/credential/review state; native acceptance remains pending.
- Add explicit absolute Windows isolated profiles with visible title/path, independent versioned JSON and current-user DPAPI secret storage. Reject malformed arguments, normal-package-container overlap, links, unknown markers and corrupt/forward-format data without normal-store fallback or destructive resets.
- Verify 78 tests including original SQL fixtures, real DPAPI/permission/link behavior, appearance byte preservation and synthetic production request routing. Controlled validation/profile/model-reload mutations fail as expected. Retain the prior 8574924 package and native evidence. New Windows builds report 277 warnings versus 272 baseline; no warnings were suppressed.

- Carry separate immutable schema/name identity through table selection, catalog extraction, relationships, metadata export and provider sampling. Keep ordinary keys stable and escape dot/bracket-containing names without delimiter collisions.
- Add format-2 raw identity manifests and typed FK/index/constraint/trigger identities; preserve existing table arrays and readable reference fields. Reject ambiguous legacy selections instead of guessing.
- Verify separate selection of real tempdb delimiter collisions, same-named composite FK/index groups, constraints/triggers/views/routines, sampling and an expected failing ambiguous-key mutation. Keep live PostgreSQL/MySQL and updated-package native checks explicit.
- Added a local Evidence PDF with a source-field manifest and sample-free schema JSON; no model request is required. Export names are unique even within one second.
- Fixed clipped relationship-map columns and supplied the Windows window title. Verified the offline demo, native JSON export and five-page format-2 PDF on Windows 11.

- Replaced hand-built connection strings and permissive TLS defaults with provider builders and an explicit local certificate exception.
- Made every remote model run show its recipient and metadata scope; excluded sample rows from all model requests and removed the Gemini key from the URL.
- Replaced overclaiming analysis prompts with evidence-limited schema topics, source paths, hypothesis labels and input budgeting.
- Preserved schema-qualified table identity, composite foreign-key pairs, ordered index keys, included columns, filters and available constraint definitions. Added safe, bounded optional sampling.
- Replaced CDN Mermaid with a self-contained relationship map that treats database names as data. Added a synthetic offline demo and cancelable model requests.
- Updated the MAUI interface, app identity/icon, README, support matrix, tests and portable CI workflow.

This entry describes local source changes. It does **not** announce a public release or certify a packaged app installation.

### Native preview reachability

- Removed the star-sized wrapper inside the main ScrollView so default-size windows can scroll to schema export actions. Native wheel scrolling and JSON export were verified at 1009×841.
- Added light/dark, opaque/translucent and 100/150/200% text controls to the offline map. List view uses unscaled, naturally scrolling cards and preserves declared foreign keys as text. Arrow keys pan the map; Home fits it; keyboard focus reveals the selected card. Controls and focus use stronger contrast, and custom hover movement was removed.
- Verified the actual Windows WebView in dark list view at 200% text, including Tab focus on the last table, automatic scrolling, local no-sample feedback and Close. This does not establish full-app theme/scaling, screen-reader or fresh-machine support.
