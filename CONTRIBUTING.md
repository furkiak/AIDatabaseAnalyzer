# Contributing

Start with the packaged offline demo. If reporting a database extraction defect, create a minimal **synthetic** SQL fixture and include the engine/version, expected JSON field and actual JSON field. Never include production schema exports, data rows, connection strings or API keys in a public issue.

For code changes, run the portable tests and the Windows build commands in [README.md](README.md). SQL Server integration tests require an isolated local instance via `AIDB_TEST_SQLSERVER`. PostgreSQL/MySQL fixtures are welcome; mark results as unverified until run against each provider. Keep model claims tied to source fields and distinguish a hypothesis from a measured result.

Open a focused pull request with the trigger, before/after behavior and validation. For a security flaw, use the private reporting path in [SECURITY.md](SECURITY.md).
