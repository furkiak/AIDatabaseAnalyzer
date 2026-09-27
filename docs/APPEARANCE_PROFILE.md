# Local application appearance and isolated review profiles

The MAUI review and model-settings pages offer **Light/Dark** and **100/150/200% text** in the visible Local appearance card. Save appearance writes only `appearance.json`, then updates real `FontSize` DynamicResources and this application's theme. It does not save model settings, clear an in-progress schema/review, change provider credentials or endpoints, change analysis timeouts, grant consent or change operating-system preferences. A 14-point base font becomes 14/21/28 points; this is not a display-DPI setting. Choosing values without saving does not change the running appearance.

Main fields and model settings use one-column layouts, naturally measured buttons, wrapping labels and vertical scrolling. The minimum window is 400×400; the initial size remains 1024×850. JSON and table lists retain bounded, scrollable viewports. Semantic descriptions identify fields, selectors, sampling, component/table/topic selection and custom-prompt removal.

### Windows native theme, navigation and button measurement

A local native review of `cb27226` found that a Light page still opened a Dark native Picker menu under a Dark OS theme, that the Shell's Review/Settings labels remained at 100% under 200% page text, and that the connection button clipped its caption at 400-pixel width. The following Windows-only source correction addresses those findings; the corrected binary still requires the same native checks.

- At `Window.Created`, and after a successful appearance save, set **this app window's content root** `FrameworkElement.RequestedTheme` to the saved Light/Dark value. Native Shell navigation roots receive the same theme. No Windows-global setting or `Microsoft.UI.Xaml.Application.RequestedTheme` is changed. Windows title-bar/OS chrome and high-contrast overrides are outside this preference.
- Use the saved `Font14` resource (14/21/28 points) for Shell navigation containers and realized caption text, including subsequently realized items. Retain the native selection, navigation and focus controls. WinUI's top-navigation row defaults to a fixed 48 pixels; its scoped minimum row space now grows with the font, preserving room for labels and focus.
- Keep MAUI's existing `MauiButton`, automation peer, image/text content and command handling. A Windows `ButtonHandler` applies real TextBlock wrapping after Text/Font/Padding mappings, allows unlimited lines without trimming, and bounds the existing content panel/text to the available width **before native measurement**. It does not assign a fixed button height. A successful build alone does not prove a wrapped caption: native 400-pixel/200% acceptance is still required.

This uses the installed **Microsoft.Maui 9.0.111** source commit `5531aa96fe88f40f612b4772fee56dceefc37932`: [window lifecycle/handler creation](https://github.com/dotnet/maui/blob/5531aa96fe88f40f612b4772fee56dceefc37932/src/Core/src/Platform/Windows/ApplicationExtensions.cs), [ShellItemHandler's native top navigation](https://github.com/dotnet/maui/blob/5531aa96fe88f40f612b4772fee56dceefc37932/src/Controls/src/Core/Handlers/Shell/ShellItemHandler.Windows.cs), [MauiButton content measurement](https://github.com/dotnet/maui/blob/5531aa96fe88f40f612b4772fee56dceefc37932/src/Core/src/Platform/Windows/MauiButton.cs), and [Button mappings](https://github.com/dotnet/maui/blob/5531aa96fe88f40f612b4772fee56dceefc37932/src/Core/src/Handlers/Button/ButtonHandler.Windows.cs). The exact **Windows App SDK 1.7.250606001** package's distributed WinUI `Themes/generic.xaml` provides `ControlContentThemeFontSize`, `NavigationViewTopPaneHeight`, the NavigationViewItem focus template and top-navigation presenter; [Microsoft's version-specific RequestedTheme contract](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.requestedtheme?view=windows-app-sdk-1.7) describes root/child theme inheritance. No native template was replaced. The offline demo label also now uses a proper Unicode em dash.

### App-owned alerts and title-bar contrast

The existing 20 app-owned alert/confirmation call sites now use `AppearanceDialogs`. On Windows each dialog explicitly receives the **saved** native theme, 14/21/28-point message/button text and 20/30/40-point title text, opaque app surfaces, wrapping content and a vertically scrollable message bounded by the current app window. Button captions can wrap, while their native focus template remains in use. Alerts queue per app window; no global Windows dialog settings or application resources are overwritten. Non-Windows calls delegate to the same MAUI `DisplayAlert` overloads as before. This adds no data write or request.

The confirmation contract follows the pinned [MAUI AlertManager](https://github.com/dotnet/maui/blob/5531aa96fe88f40f612b4772fee56dceefc37932/src/Controls/src/Core/Platform/AlertManager/AlertManager.Windows.cs): accept is the primary button, cancel is secondary, the default button is **None**, and only the primary result returns `true`; Escape/dismissal returns `false`. One-button notifications retain the secondary dismiss button. Message/title text and labels at each call site are unchanged. In particular, opening or dismissing model consent does not grant consent, alter the recipient or make a model request.

Local review of `634ef1a` verified a Light native Picker popup/chevron, 21/28-point Shell labels, and the complete two-line connection caption at 400×400/Dark/200%. It also exposed app-title/caption contrast under the opposite OS theme and an unscaled MAUI alert. The follow-up source sets only the owned `AppWindow.TitleBar` active/inactive/button foreground/background colors and the existing MAUI title text/container colors at startup, appearance save, root load and activation. It keeps the framework title/drag template and OS caption font; it does not change an operating-system theme. [Pinned MAUI title implementation](https://github.com/dotnet/maui/blob/5531aa96fe88f40f612b4772fee56dceefc37932/src/Core/src/Platform/Windows/MauiWinUIWindow.cs) explains why native caption colors formerly followed the OS application theme. The follow-up dialog/title-bar binary still requires native acceptance; the earlier verified binary remains available.

Native surfaces are opaque; no blur or custom motion was added. Native focus behavior comes from WinUI controls. The application palette has the following **source-calculated**, sRGB contrast minima across page/card/input backgrounds; this is not a screenshot measurement or an accessibility certificate:

| Foreground role | Light | Dark |
| --- | ---: | ---: |
| Body text | 12.89:1 | 11.93:1 |
| Secondary text | 6.74:1 | 8.28:1 |
| Field/button outline | 4.20:1 | 5.36:1 |

White primary-button text against `#2258C3` is 6.46:1. Accent and warning text also exceed 4.5:1 on those surfaces (overall text minimum 5.73:1). Actual focus rings, native picker menus, disabled states, high-contrast themes, reading order and 200% layout need native review. The already-validated offline map HTML, its current-map preferences, identifier escaping and navigation behavior are unchanged; its own theme/text controls remain independent of MAUI appearance.

## Explicit isolated profile

For a local Windows review, supply a fresh **absolute** directory. This example
uses a path under your temporary folder:

```powershell
$reviewProfile = Join-Path $env:TEMP 'AIDatabaseAnalyzer-Review'
.\AIDatabaseAnalyzer.exe --data-directory $reviewProfile
```

Use a new, empty directory. The window title and appearance card identify the
isolated profile. There is no automatic demo, database connection or model
request: select **Try the offline demo** explicitly. The synthetic demo has
three tables/two foreign keys and no sample rows.

- No arguments retain the existing normal MAUI Preferences/SecureStorage model settings. The new normal appearance file defaults to Light/100% without rewriting legacy model preferences.
- An isolated profile never initializes the normal settings adapter. It has its own versioned `settings.json`, `appearance.json` and `.aidb-profile` ownership marker. An API key, if explicitly saved, is encrypted in `secrets.dpapi.json` using **Windows current-user DPAPI**; plaintext fallback is absent. This is user/machine protection, not a portable credential vault. No real key is needed for the offline demo or appearance review.
- Relative/malformed/duplicate arguments, a directory overlapping the normal Windows package container (Data/Settings/Cache), unowned nonempty directories and unsupported markers are rejected. Symbolic links/junctions in any ancestor and hard-linked profile files are rejected before reading/writing. Invalid arguments do not fall back to the normal profile.
- Files have a 64-KiB input limit; duplicate JSON keys, unsupported versions, malformed appearance values and unreadable DPAPI data do not become defaults or silently overwrite current/backup data. Missing appearance fields in version 1 retain Light/100% compatibility. Unreadable appearance prevents startup; unreadable model settings disable their Save/Test commands and show a sanitized status. Preserve/repair the selected profile's data before retrying; the app does not erase it.
- Successful replacements use a flushed temporary file and atomic replacement, keeping the prior file as `.bak`. Failed/locked/read-only operations retain existing data. Model fields retain their existing individual-save semantics; a partial model save is reported explicitly. Appearance has its own independent transaction.

Profile isolation covers application-owned appearance/model settings and credentials. It is not an operating-system sandbox: database inputs remain transient, exports retain their existing Desktop/share destination, and MAUI/WebView/runtime storage is managed by the framework. No OS settings, startup entry or account is changed by appearance saving.

## Earlier appearance evidence and remaining checks

The earlier .NET 9 appearance revision passed **78/78 Windows tests**, zero
failed/skipped: the original 29 cases plus 49 profile, appearance and
request-routing cases. Its SQL fixture used `AIDB_TEST_SQLSERVER=.\SQLEXPRESS`.
The added tests covered actual Windows DPAPI, profile isolation, atomic-save
failures, malformed or forward-format data, linked paths and real font sizes.
Two model-request tests used a synthetic HTTP handler, not a real model.
The newer .NET 10 package passed **86/86 tests** and selected native flows; see
[runtime validation](RUNTIME.md) for the exact commit, package and limits.

Four controlled mutations produced expected failures: narrowing the normal-package reservation back to Data only (6), removing font-save validation (3), selecting normal storage for an isolated profile (1), and freezing the Ollama model instead of reloading settings (1). Restoring production sources returned all 78 tests to green. No cosmetic XAML-string tests were added.

The initial .NET 9 appearance source still had **277 build warnings**. The
later .NET 10 revision resolved the compiler and binding warnings and built
with zero warnings. Non-Windows runtime and fresh-machine installation remain
unverified; neither historical build result should be mistaken for those checks.

The wider native acceptance still includes:

1. Launch the fresh isolated profile, confirm its title/path, and choose Dark/200%; Save appearance. Inspect Main and Settings at 400-pixel width with scrolling and keyboard focus, including picker menus and wrapped primary actions.
2. Load the offline demo; retain its JSON while switching tabs and changing Light/150%. Return to preview, scroll to JSON/PDF/map actions, and confirm the schema is unchanged. Do not invoke Connect/Test Connection/Generate review merely to test appearance.
3. Reopen the same profile and verify saved theme/font. Verify app-owned model/secret files were not created or changed by appearance-only steps.
4. Exercise the existing independent map controls and Close; export JSON/local Evidence PDF if desired and compare against the demo. Export behavior remains unchanged.

The .NET 10 runtime record covers selected native steps above, but not this
entire appearance, dialog and accessibility matrix. No real-model result or
public release is claimed.
