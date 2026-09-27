# Metadata format 2: separate object identity

Live table selection uses an immutable `DatabaseObjectIdentity(Schema, Name)`. Both catalog fields are returned separately by every provider query. Selection, FK/index grouping and sample queries compare/use the raw pair; they never split a display string on a dot. The table checkbox keeps the pair even though its label shows a readable key.

The existing export containers remain: `Tables` maps keys to column arrays, FK `From`/`To` and index/constraint/trigger `Table` refer to those keys, `Views` is a key list and `StoredProcedures` maps keys to parameter-name lists. Format 2 adds:

- `MetadataFormat`: version, identity fields, key convention and scope.
- `TableIdentities`, `ViewIdentities`, `RoutineIdentities`: maps from the displayed key to separate raw `Schema` and `Name`.
- FK `FromIdentity`/`ToIdentity`, index/constraint/trigger `TableIdentity`, and trigger `Identity`: raw pairs attached to related records.
- Trigger `Disabled`: SQL Server catalog state; `null` for providers where that state is not collected.

These are catalog names, not sample rows. They remain in sample-free model/Evidence PDF payloads. No sample value is joined into an identity map. Definitions and object names can themselves be sensitive; review them before sharing. Views and routines cover visible database-wide catalog scope when their component is enabled; table-linked components cover selected source tables.

## Readable, injective keys

Ordinary `dbo.Table` keys stay unchanged. If **either** component contains `.`, `[` or `]`, both parts are bracket-quoted and each literal `]` becomes `]]`:

| Raw schema | Raw name | Key |
| --- | --- | --- |
| `dbo` | `Table` | `dbo.Table` |
| `a.b` | `c` | `[a.b].[c]` |
| `a` | `b.c` | `[a].[b.c]` |
| `[a]` | `b` | `[[a]]].[b]` |

This is a portable export/display convention. PostgreSQL sampling still uses double-quoted raw parts and MySQL sampling uses backtick-quoted raw parts. SQL Server uses bracket-quoted raw parts. Display keys are never interpolated into provider SQL.

The existing `GetTablesAsync` and `SchemaFilterConfig.SelectedTables` string APIs remain compatibility adapters. New callers should use `GetTableIdentitiesAsync` and `SelectedTableIdentities`. Ordinary unambiguous legacy keys and canonical escaped keys are accepted. An old `a.b.c` key cannot identify which component owns the dot and is rejected. Noncanonical `[a].[b]` aliases are also rejected because an old string could represent literal bracket-containing names. Reload the table list or pass the raw typed pair; do not guess from an old export or rewrite the original file.

## Local evidence and limits

The Windows/.NET 9 test run passes 29 cases, including three actual SQL Server 2022 Express cases. The new delimiter fixture uses two unique owned `tempdb` schemas in the shape `[a.b].[c]` and `[a].[b.c]`, extracts each separately and together, and verifies sample markers, raw identity maps, same-named composite FK/index groups, key order/include/filter, check/default expressions, triggers, dotted views and dotted routines/parameter lists. Cleanup targets only its GUID-derived objects. SQL fixture classes run sequentially because concurrent catalog reads plus test DDL can deadlock each other's tempdb catalog locks.

The old ambiguous-key mutation turned the two-table export into one table and made the real integration test fail. Restoring the canonical implementation made the full suite pass. Portable tests cover 121 dot/bracket/literal-pattern pairs and provider-specific raw sampling quotation. Those are not live PostgreSQL/MySQL tests. Existing two-schema homonyms, composite relationships/indexes, escaped closing-bracket sampling, TLS rejection and privacy/export tests remain covered.

Metadata collection still does not read view/trigger/routine bodies, plans, workloads, row counts or permissions. PostgreSQL routine overloads with the same schema/name are not separated by signature in this format. Referential actions and index sort direction are not modeled. A concurrent external DDL change may make catalog extraction fail; this is not a transactional catalog snapshot or a concurrent-DDL guarantee. Fresh-machine install and actual PostgreSQL/MySQL runtime validation remain separate checks.
