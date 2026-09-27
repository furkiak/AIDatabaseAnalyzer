# Runtime and dependency policy

This branch targets .NET 10 and pins the MAUI Controls packages to 10.0.101. The
SDK selection in `global.json` allows installed stable .NET 10 feature bands,
without selecting .NET 11 or a preview SDK. The Windows runtime candidate is
1.3.0-preview.1; it does not change metadata format 2 or isolated-profile formats.
Existing profiles must not be deleted to perform an upgrade.

The initial .NET 10 Windows build retained 277 warnings. Typed page/item bindings,
partial observable properties, explicit window creation and nullable response
handling now give a local build with zero warnings/errors and 86 passing tests.
Eight empty/non-text answer regressions failed before the production parser fix.
Compiler warnings fail builds; no warning category is suppressed. These source
checks do not establish NativeAOT, measured performance or final-package native
acceptance.

## Dated final-package validation

On 27 September 2026 the clean clone of source commit
`444b863ee53c0d03a4205fa5dbf0513ddbecb91f` passed **86/86 tests**, zero failures
or skips, including three actual SQL Server cases. Publish reported zero warnings
and errors. The accepted self-contained Windows x64 package is
`AI-Database-Analyzer-1.3.0-preview.1-444b863-win-x64-complete.zip` (129,373,048
bytes), SHA-256
`D0D4F5BEC77D11886BCA6A036A6DA71BAF19CDE6EE4E9E0086518C8B69899F3E`.
It contains .NET **10.0.12** and MAUI **10.0.101**.

The exact final ZIP was launched in an owned isolated Windows profile. Selected
Dark/200 demo/Approve commands, two-way topic selection and Add Prompt (4/5 to
5/6), Go Back, Light/200 appearance-only Save with retained schema/no model
settings, matching actual JSON and the five-page Evidence PDF passed. Every PDF
page was rendered and visually read. The Light/100 WebView showed three tables
and two foreign keys; Close returned to the review. Restart retained Light/200;
both owned test processes ended. The four known normal-profile file hashes or
absence states stayed unchanged. No personal history was copied and no model
request was made.

The dated local records are outside this repository, under
`E:\GithubDosyalar_Kurtarma_2026-09-27\_GithubProfesyonelHedef\evidence`:

- `AIDatabaseAnalyzer-package-444b863-net10.json` binds the code commit, ZIP,
  clean-test/publish proof and native result.
- `Runtime-net10-2026-09-27\AIDatabaseAnalyzer-444b863\native-result.json`
  records the exact-package observations and remaining boundaries.

This documentation follow-up records those existing results. It does not rebuild,
repackage or rerun the accepted code/ZIP; later documentation-only HEADs are not
new runtime acceptance. The wider `87d493d`/.NET 9 minimum-window, dialog/keyboard
and ReadOnly matrix remains historical. Native Remove Prompt, two-button model
consent, all dialogs/inactive title, DPI/screen readers, additional live database
or model providers, NativeAOT, other OS and fresh-machine checks remain open.
Remote CI, push and public release have not been completed by this validation.

.NET 10's LTS lifetime does **not** extend the lifetime of MAUI 10. Microsoft's
[MAUI support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui),
checked on 27 September 2026, lists MAUI 10.0.101 and support through 11 May 2027;
MAUI 9 support ended on 12 May 2026. The
[runtime support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
lists .NET 10 support through 14 November 2028. Recheck servicing versions and
the MAUI schedule before each public release.

Install a stable .NET 10 SDK and the MAUI Windows workload for a Windows build:

```powershell
dotnet workload install maui-windows
dotnet build AIDatabaseAnalyzer/AIDatabaseAnalyzer.csproj -c Release -f net10.0-windows10.0.19041.0 -p:TargetFrameworks=net10.0-windows10.0.19041.0
dotnet test AIDatabaseAnalyzer.ConnectionTests/AIDatabaseAnalyzer.ConnectionTests.csproj -c Release --filter 'Category!=SqlServer'
```

An SDK/workload registration alone does not prove the required packs exist.
Repair missing packs using the official `dotnet workload` command; do not copy
SDK assemblies between installations or suppress missing-workload checks. This
machine initially reported MAUI installed while its .NET 10 SDK pack was absent.

The full test run needs the explicitly named, disposable SQL Server instance
described in the README. Without it, three integration tests fail rather than
silently skipping. The CI Windows build and portable-test checks remain separate
from SQL Server and actual application acceptance.

All projects enable direct and transitive NuGet vulnerability auditing. NU1901,
NU1902, NU1903 and NU1904 stop restore/build. This depends on the configured feeds
and their current advisory data; it does not prove absence of unknown flaws.
Use `--no-restore` when auditing an already restored Windows-only graph so a
report does not try to restore unrelated experimental mobile targets:

```powershell
dotnet list AIDatabaseAnalyzer/AIDatabaseAnalyzer.csproj package --no-restore --framework net10.0-windows10.0.19041.0 --vulnerable --include-transitive
dotnet list AIDatabaseAnalyzer.ConnectionTests/AIDatabaseAnalyzer.ConnectionTests.csproj package --no-restore --vulnerable --include-transitive
```

The previous 87d493d .NET 9 Windows package and its native demo/dialog/export
evidence remain historical. Native tests on one runtime must not be presented
as acceptance of a new-runtime ZIP. Fresh-machine installation, other operating
systems, display DPI, screen readers, real model requests and live hosting are
separate gates. Use a new explicit isolated profile for local native tests; do
not migrate or test against a person's normal profile without a separate review.

## Türkçe

Kabul edilen kaynak commit'i `444b863`, kesin ZIP ve SHA-256 değeri yukarıdadır.
86 temiz klon testi ve sıfır uyarılı publish dışında, aynı ZIP'in yalnız belirtilen
Windows demo/konu sayacı, görünüm kaydı, JSON/PDF, WebView ve yeniden açılış
akışları 27 Eylül 2026'da doğrulandı. Bu belge commit'i yeni derleme veya paket
kabulü değildir. Eski .NET 9 matrisi tarihsel kalır; tüm onay/dialog, DPI, ekran
okuyucu, gerçek model/ek veritabanı, diğer OS, temiz makine, uzak CI, push ve
yayın kapıları açıktır.
