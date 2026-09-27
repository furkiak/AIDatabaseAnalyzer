# Local application appearance and isolated review profiles

The MAUI review and model-settings pages offer **Light/Dark** and **100/150/200% text** in the visible Local appearance card. Save appearance writes only `appearance.json`, then updates real `FontSize` DynamicResources and this application's theme. It does not save model settings, clear an in-progress schema/review, change provider credentials or endpoints, change analysis timeouts, grant consent or change operating-system preferences. A 14-point base font becomes 14/21/28 points; this is not a display-DPI setting. Choosing values without saving does not change the running appearance.

Main fields and model settings use one-column layouts, naturally measured buttons, wrapping labels and vertical scrolling. The minimum window is 400×400; the initial size remains 1024×850. JSON and table lists retain bounded, scrollable viewports. Windows button text wraps inside MAUI's real native button content, following its [ButtonExtensions implementation](https://github.com/dotnet/maui/blob/9.0.110/src/Core/src/Platform/Windows/ButtonExtensions.cs). Semantic descriptions identify fields, selectors, sampling, component/table/topic selection and custom-prompt removal.

Native surfaces are opaque; no blur or custom motion was added. Native focus behavior comes from WinUI controls. The application palette has the following **source-calculated**, sRGB contrast minima across page/card/input backgrounds; this is not a screenshot measurement or an accessibility certificate:

| Foreground role | Light | Dark |
| --- | ---: | ---: |
| Body text | 12.89:1 | 11.93:1 |
| Secondary text | 6.74:1 | 8.28:1 |
| Field/button outline | 4.20:1 | 5.36:1 |

White primary-button text against `#2258C3` is 6.46:1. Accent and warning text also exceed 4.5:1 on those surfaces (overall text minimum 5.73:1). Actual focus rings, native picker menus, disabled states, high-contrast themes, reading order and 200% layout need native review. The already-validated offline map HTML, its current-map preferences, identifier escaping and navigation behavior are unchanged; its own theme/text controls remain independent of MAUI appearance.

## Explicit isolated profile

For a local Windows review, supply a fresh **absolute** directory:

```powershell
.\AIDatabaseAnalyzer.exe --data-directory "E:\GithubDosyalar_Kurtarma_2026-09-27\AIDatabaseAnalyzer\TestResults\native-appearance-profile"
```

This example is a launch instruction, not evidence that the new application was launched. The window title and appearance card identify the isolated profile. There is no automatic demo, database connection or model request: select **Try the offline demo** explicitly. The synthetic demo has three tables/two foreign keys and no sample rows.

- No arguments retain the existing normal MAUI Preferences/SecureStorage model settings. The new normal appearance file defaults to Light/100% without rewriting legacy model preferences.
- An isolated profile never initializes the normal settings adapter. It has its own versioned `settings.json`, `appearance.json` and `.aidb-profile` ownership marker. An API key, if explicitly saved, is encrypted in `secrets.dpapi.json` using **Windows current-user DPAPI**; plaintext fallback is absent. This is user/machine protection, not a portable credential vault. No real key is needed for the offline demo or appearance review.
- Relative/malformed/duplicate arguments, a directory overlapping the normal app-data directory, unowned nonempty directories and unsupported markers are rejected. Symbolic links/junctions in any ancestor and hard-linked profile files are rejected before reading/writing. Invalid arguments do not fall back to the normal profile.
- Files have a 64-KiB input limit; duplicate JSON keys, unsupported versions, malformed appearance values and unreadable DPAPI data do not become defaults or silently overwrite current/backup data. Missing appearance fields in version 1 retain Light/100% compatibility. Unreadable appearance prevents startup; unreadable model settings disable their Save/Test commands and show a sanitized status. Preserve/repair the selected profile's data before retrying; the app does not erase it.
- Successful replacements use a flushed temporary file and atomic replacement, keeping the prior file as `.bak`. Failed/locked/read-only operations retain existing data. Model fields retain their existing individual-save semantics; a partial model save is reported explicitly. Appearance has its own independent transaction.

Profile isolation covers application-owned appearance/model settings and credentials. It is not an operating-system sandbox: database inputs remain transient, exports retain their existing Desktop/share destination, and MAUI/WebView/runtime storage is managed by the framework. No OS settings, startup entry or account is changed by appearance saving.

## Current evidence and native handover

Windows tests pass **69/69, zero failed/skipped**: all original 29 cases plus 40 profile/appearance/request-routing cases. The existing certificate-rejection SQL case and both uniquely owned `tempdb` extraction fixtures ran against `AIDB_TEST_SQLSERVER=.\SQLEXPRESS`. New cases include actual Windows DPAPI restart/byte preservation, read-only/locked atomic-save failures, malformed/forward-format/duplicate JSON preservation, junction/hard-link rejection, zero normal-store-factory access, separate profiles and real numeric font sizes. The two new production LlmService request-path cases use a **synthetic HttpMessageHandler** and prove settings/request routing, not real model or network behavior.

Three controlled mutations produced expected failures: removing font-save validation (3), selecting normal storage for an isolated profile (1), and freezing the Ollama model instead of reloading settings (1). Restoring production sources returned all 69 tests to green. No cosmetic XAML-string tests were added.

Windows Release build/publish evidence is recorded under ignored `TestResults`; warnings are retained. The previous 8574924 source was rebuilt separately with the same flags: **272 warnings, zero errors**. The updated build has **277 warnings, zero errors**: five additional XC0022 binding-performance warnings (four semantic-selection bindings and the model-settings load-status binding), with no additional C# warning category. Existing nullable/obsolete/MVVM/compiled-binding warnings remain unresolved. Non-Windows build/runtime and fresh-machine installation are not verified.

Root native acceptance remains:

1. Launch the fresh isolated profile, confirm its title/path, and choose Dark/200%; Save appearance. Inspect Main and Settings at 400-pixel width with scrolling and keyboard focus, including picker menus and wrapped primary actions.
2. Load the offline demo; retain its JSON while switching tabs and changing Light/150%. Return to preview, scroll to JSON/PDF/map actions, and confirm the schema is unchanged. Do not invoke Connect/Test Connection/Generate review merely to test appearance.
3. Reopen the same profile and verify saved theme/font. Verify app-owned model/secret files were not created or changed by appearance-only steps.
4. Exercise the existing independent map controls and Close; export JSON/local Evidence PDF if desired and compare against the demo. Export behavior remains unchanged.

These checks have not been performed by the background agent. The source work does not claim a new native screenshot, installation, real model result or public release.
