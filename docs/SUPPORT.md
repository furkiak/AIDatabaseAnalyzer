# Platform and runtime support

Snapshot: 22 September 2026.

| Target | Build evidence | Runtime/installation evidence | Status |
| --- | --- | --- | --- |
| Windows 10 19041+ on .NET 9 | Local `dotnet build -f net9.0-windows10.0.19041.0` passed; 254 existing nullable/XAML warnings | Packaged app and interactive UI not yet tested | Development preview |
| Android | Target declared; matching .NET 9 workload not available in this test setup | None | Experimental |
| iOS | Target declared; Windows host cannot validate iOS build/run | None | Experimental |
| Mac Catalyst | Target declared; Windows host cannot validate Mac build/run | None | Experimental |

The 22 test cases include SQL Server 2022 Express integration and portable connection/privacy/diagram tests. PostgreSQL and MySQL are implemented but not yet exercised against real servers. The CI workflow runs only portable tests; it does not certify a MAUI app build.

[Microsoft's .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy) lists .NET 9 support through **10 November 2026** and .NET 10 LTS through **14 November 2028**. A .NET 10 migration should be tested on a compatible Visual Studio/MAUI toolchain before a public release. This repository deliberately keeps the validated Windows .NET 9 build until that migration has build and installation evidence; no unsupported claim is made for the other platforms.
