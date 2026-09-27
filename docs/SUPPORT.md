# Platform and runtime support

Snapshot: 27 September 2026.

| Target | Build evidence | Runtime/installation evidence | Status |
| --- | --- | --- | --- |
| Windows 10 19041+ on .NET 9 | Local `dotnet build -f net9.0-windows10.0.19041.0` passed; existing nullable/XAML/MVVM analyzer warnings (272 in the identity-update Release build) | Windows 11 launch; prior native offline demo, JSON export, WebView diagram and Evidence PDF. Updated identity package needs its own quick native check. No fresh-machine install | Development preview |
| Android | Target declared; matching .NET 9 workload not available in this test setup | None | Experimental |
| iOS | Target declared; Windows host cannot validate iOS build/run | None | Experimental |
| Mac Catalyst | Target declared; Windows host cannot validate Mac build/run | None | Experimental |

The 29 test cases include three actual SQL Server 2022 Express cases, delimiter-safe typed identity, composite FK/index metadata, and portable connection/privacy/diagram tests. PostgreSQL and MySQL catalog queries return schema/name separately; raw identifier quotation is tested, but the providers have not been exercised against real servers. The CI workflow runs only portable tests; it does not certify a MAUI app build. [Metadata format 2](METADATA_FORMAT.md) records scope, compatibility and remaining metadata limits.

[Microsoft's .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) lists .NET 9 support through **10 November 2026** and .NET 10 LTS through **14 November 2028**. A .NET 10 migration should be tested on a compatible Visual Studio/MAUI toolchain before a public release. This repository deliberately keeps the validated Windows .NET 9 build until that migration has build and installation evidence; no unsupported claim is made for the other platforms.
