# Runtime and dependency policy

This branch targets .NET 10 and pins the MAUI Controls packages to 10.0.101. The
SDK selection in `global.json` allows installed stable .NET 10 feature bands,
without selecting .NET 11 or a preview SDK. The Windows runtime candidate is
1.3.0-preview.1; it does not change metadata format 2 or isolated-profile formats.
Existing profiles must not be deleted to perform an upgrade.

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
