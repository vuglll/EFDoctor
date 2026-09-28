# Suppressing findings

Use standard Roslyn suppression. Always record why the reported pattern is intentional.

For the smallest source region, use a pragma:

```csharp
#pragma warning disable EFD001 // Intentional per-item transaction boundary.
foreach (var item in items)
{
    context.SaveChanges();
}
#pragma warning restore EFD001
```

For a file or directory scope, use `.editorconfig` and place the justification in a nearby comment or engineering record:

```ini
[IntentionalPerItemWriter.cs]
# Reviewed: each item must commit independently for partial-failure isolation.
dotnet_diagnostic.EFD001.severity = none
```

Where the Roslyn host supports it, use `SuppressMessage` with its `Justification` property:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "EFD001",
    Justification = "Each iteration is an intentional independent transaction.")]
void SaveIndependently(DbContext context, IEnumerable<Item> items)
{
    foreach (var item in items)
    {
        context.SaveChanges();
    }
}
```

EFDoctor has no proprietary suppression file.

`SuppressMessage` matches on the rule's category as well as its ID. Most rules use `Performance`; EFD012 uses `Security`, EFD014, EFD017, EFD018, and EFD022 use `Correctness`, EFD021 uses `Reliability`, and EFD025 uses `Maintainability`. Each rule page shows the exact attribute.

For EFD002, use the same mechanisms with diagnostic ID `EFD002`. Examples and guidance for positive, empty-set, synchronous, and asynchronous existence checks are in [`docs/rules/EFD002.md`](rules/EFD002.md).

For EFD003, use diagnostic ID `EFD003` and record why the index is intentionally absent from the EF model—for example, because an equivalent DBA-managed index was verified. See [`docs/rules/EFD003.md`](rules/EFD003.md) for pragma, `.editorconfig`, and `SuppressMessage` examples.

For EFD004, use diagnostic ID `EFD004` and record why the client-side materialization boundary is intentional—for example, because a small bounded result is reused or the provider cannot translate required logic. See [`docs/rules/EFD004.md`](rules/EFD004.md) for suppression examples.

For EFD005, use diagnostic ID `EFD005` and record why complete materialization is intentional—for example, for a controlled export or a known-small lookup table. See [`docs/rules/EFD005.md`](rules/EFD005.md) for suppression examples.

For EFD006, use diagnostic ID `EFD006` and record why single-query loading is intentional or why split-query behavior is configured elsewhere. See [`docs/rules/EFD006.md`](rules/EFD006.md) for suppression examples.

For EFD009, use diagnostic ID `EFD009` and record why the transformation is acceptable—for example, because an expression index on `LOWER(column)` backs the lookup. See [`docs/rules/EFD009.md`](rules/EFD009.md) for suppression examples.

For EFD010, use diagnostic ID `EFD010` and record why the synchronous call is acceptable—for example, because it runs off the hot request path such as startup seeding or a single-threaded tool. See [`docs/rules/EFD010.md`](rules/EFD010.md) for suppression examples.

For EFD011, use diagnostic ID `EFD011` and record why the synchronous integration boundary cannot propagate async. See [`docs/rules/EFD011.md`](rules/EFD011.md) for suppression examples.

For EFD012, use diagnostic ID `EFD012` only after confirming that every dynamic fragment comes from a fixed allow-list rather than user input. See [`docs/rules/EFD012.md`](rules/EFD012.md) for suppression examples.

For EFD013, use diagnostic ID `EFD013` and record why per-entity tracking is required—for example, because save interceptors, domain events, or concurrency tokens must run for each row. See [`docs/rules/EFD013.md`](rules/EFD013.md) for suppression examples.

For EFD014, use diagnostic ID `EFD014` and record why an undefined row order is acceptable—for example, an intentionally unordered sample over a small table. See [`docs/rules/EFD014.md`](rules/EFD014.md) for suppression examples.

For EFD017, use diagnostic ID `EFD017` and record why the include is kept even though the projection ignores it. See [`docs/rules/EFD017.md`](rules/EFD017.md) for suppression examples.

For EFD018, use diagnostic ID `EFD018` and record why discarding the task is safe—for example, because it runs on a separately scoped context. See [`docs/rules/EFD018.md`](rules/EFD018.md) for suppression examples.

For EFD019, use diagnostic ID `EFD019` and record why buffering the full result is acceptable—for example, for a known-tiny configuration table. See [`docs/rules/EFD019.md`](rules/EFD019.md) for suppression examples.

For EFD020, use diagnostic ID `EFD020` and record why re-executing the query is acceptable—for example, because the second read must observe data changed by a save in between. See [`docs/rules/EFD020.md`](rules/EFD020.md) for suppression examples.

For EFD021, use diagnostic ID `EFD021` and record why static state is acceptable—for example, a single-threaded console tool that disposes the context on exit. See [`docs/rules/EFD021.md`](rules/EFD021.md) for suppression examples.

For EFD022, use diagnostic ID `EFD022` only when the query is deliberately materialized and the comparison runs on the client. See [`docs/rules/EFD022.md`](rules/EFD022.md) for suppression examples.

For EFD023, use diagnostic ID `EFD023` and record why a scan is acceptable—for example, because the table is small or a trigram or full-text index already backs the search. To turn the advisory rule off entirely, set `dotnet_diagnostic.EFD023.severity = none` in `.editorconfig`. See [`docs/rules/EFD023.md`](rules/EFD023.md) for suppression examples.

For EFD025, use diagnostic ID `EFD025` and record why the redundant include is kept—for example, to mirror a generated query template. To turn the cleanup rule off entirely, set `dotnet_diagnostic.EFD025.severity = none` in `.editorconfig`. See [`docs/rules/EFD025.md`](rules/EFD025.md) for suppression examples.
