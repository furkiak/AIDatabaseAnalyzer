# Platform and runtime support

Snapshot: 27 September 2026.

| Target | Build evidence | Runtime/installation evidence | Status |
| --- | --- | --- | --- |
| Windows 10 19041+ on .NET 10 / MAUI 10.0.101 | Local Release build passed; zero warnings/errors; 86 tests with three actual SQL Server cases, zero skips; map helper builds without warnings | New-runtime final-package native acceptance and fresh-machine installation pending. 87d493d .NET 9 native demo/dialog/JSON/PDF checks remain historical | Development preview |
| Android | .NET 10 target declared; no app build in this revision | None | Experimental |
| iOS | Target declared; Windows host cannot validate iOS build/run | None | Experimental |
| Mac Catalyst | Target declared; Windows host cannot validate Mac build/run | None | Experimental |

The 86 test cases retain all 29 prior cases, including three actual SQL Server 2022 Express cases, delimiter-safe typed identity, composite FK/index metadata and portable connection/privacy/diagram tests. Forty-nine additional cases cover isolated/appearance persistence, actual Windows DPAPI/link/permission behavior and production request routing through a synthetic handler (not a real model). Eight more production-service handler cases reject null, empty, whitespace and non-text provider answers; they do not measure real generation quality. [Application appearance and isolated profiles](APPEARANCE_PROFILE.md) documents native acceptance. PostgreSQL/MySQL have not been exercised against real servers. The CI workflow now defines separate portable tests and a Windows MAUI build, but has not run remotely for this branch and does not run SQL fixtures or native acceptance. [Metadata format 2](METADATA_FORMAT.md) records compatibility and remaining metadata limits.

[Microsoft's MAUI support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/maui) lists MAUI 9 as out of support since **12 May 2026** and MAUI 10.0.101 as supported through **11 May 2027**. The [runtime policy](https://dotnet.microsoft.com/en-us/platform/support/policy) lists .NET 10 LTS support through **14 November 2028**. The longer runtime lifetime does not extend MAUI support until 2028. See [runtime policy](RUNTIME.md) for servicing and validation steps. New-runtime native/package and fresh-machine checks remain open; no support guarantee is inferred for experimental targets.
