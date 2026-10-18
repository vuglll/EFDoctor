# Proposal

## Why

EF Core 8 added `Database.SqlQueryRaw<T>(sql, params object[])` for queries that return unmapped types. Building its SQL text with interpolation or concatenation is the same injection risk that EFD012 reports for `FromSqlRaw` and `ExecuteSqlRaw`, but EFD012 doesn't recognize the method, so `context.Database.SqlQueryRaw<int>($"SELECT ... {id}")` is silently missed. The roadmap lists this as a raw-SQL coverage gap.

## What Changes

- EFD012 also reports dynamically constructed SQL passed to `RelationalDatabaseFacadeExtensions.SqlQueryRaw<TResult>`. The method is resolved by symbol, in both reduced and static forms, and its SQL origin is followed through locals exactly as for the existing raw APIs.
- `SqlQuery<TResult>(FormattableString)`, the interpolation-safe counterpart, is listed with the other safe APIs, which aren't reported.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd012-unsafe-raw-sql`: adds `SqlQueryRaw` to the supported raw APIs, and `SqlQuery` to the safe forms.

## Impact

- **Code:** `UnsafeRawSqlAnalyzer.IsSupportedRawMethod`.
- **Tests:** positive and negative `SqlQueryRaw` and `SqlQuery` analyzer cases.
- **Docs:** `docs/rules/EFD012.md`, the README and `PACKAGE.md` rule rows, the roadmap's cross-cutting list, and the CHANGELOG.
