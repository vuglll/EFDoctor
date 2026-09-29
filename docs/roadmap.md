# Roadmap

EFDoctor finds EF Core performance and correctness problems that survive code review. It runs locally, and every finding carries its evidence, a confidence level, and a remediation. This page lists what is shipped, what could come next, and how a rule earns its place.

Nothing below is a promise. A candidate becomes a rule only when it passes the quality bar.

## Principles

- **Precision over rule count.** A few trusted rules beat dozens of noisy ones. A noisy rule is the fastest way to lose the trust of experienced developers.
- **Evidence over guesses.** Every finding shows what matched, where, and why it matters.
- **Context-aware guidance.** Remediation names the legitimate exceptions.
- **Local first.** Source code and database information never leave the machine.

## Shipped

| Area | Rules |
|---|---|
| Round trips and batching | EFD001 `SaveChanges` in a loop, EFD013 load-modify-save that could be `ExecuteUpdate`/`ExecuteDelete` |
| Query shape | EFD002 `Count` for existence, EFD004 premature materialization, EFD005 unbounded materialization, EFD006 cartesian `Include`, EFD019 materialize-then-reduce, EFD020 repeated enumeration, EFD025 redundant `Include` |
| Correctness | EFD014 unordered pagination, EFD017 `Include` dropped by `Select`, EFD022 untranslatable `StringComparison`, EFD029 `OrderBy` that discards an earlier ordering |
| Async and lifetime | EFD010 sync database call in async code, EFD011 blocking on EF async, EFD018 unawaited EF task, EFD021 static `DbContext`, EFD027 concurrent operations on one `DbContext` |
| Safety | EFD012 raw SQL built from interpolation or concatenation |
| Indexing and sargability | EFD003 foreign key without an index (SQL Server, PostgreSQL), EFD009 `ToLower`/`ToUpper` on a column, EFD023 leading-wildcard search (advisory) |

Each rule's contract is in `openspec/specs/`, and its reference page is in [`docs/rules/`](rules/).

## The quality bar

Every rule defines:

1. **Trigger:** the exact syntax, semantic model, or EF metadata that produces a finding.
2. **Non-trigger:** legitimate variants that must not be reported.
3. **Evidence:** the file, line, and expression or model element, and why it matched.
4. **Impact:** the likely database or application cost, without claiming a measured impact.
5. **Remediation:** a safe suggestion, plus the cases where it does not apply.
6. **Confidence:** high, medium, or advisory.
7. **Tests:** positive, negative, edge-case, and regression fixtures.

A rule ships only when:

- it has at least ten positive and ten negative fixtures, with no known false positive among the negatives;
- it reaches at least 90% precision on a corpus of real EF Core projects, where advisory findings don't count as high-confidence results;
- every finding points to actionable evidence, not just a generic best practice;
- an intentional finding can be suppressed with a documented mechanism.

The corpus and the harness that measures precision are in [`validation/`](../validation/).

## Candidate rules

Value, static detectability, and false-positive risk are estimates. Rules marked advisory would ship at advisory confidence to protect trust.

| ID | Candidate | Value | Detectability | False-positive risk | Tier |
|---|---|---|---|---|---|
| EFD028 | Client-only (unmapped, user-defined) method inside a SQL-translated predicate or ordering | High | Medium-High | Low-Medium | Next |
| EFD030 | `IQueryable` returned from a method that disposes its `DbContext` before the caller enumerates it | High | High | Low | Soon |
| EFD031 | LINQ composition over a stored-procedure call in `FromSqlRaw` | Medium | High | Very low | Soon |
| EFD032 | Entity loaded with `AsNoTracking`, modified, then saved, so the change is silently lost | High | Medium | Medium | Later (needs result flow) |
| EFD033 | `DbContext` captured by a dependency-injection singleton | High | Medium | Low | Later |
| EFD034 | `Database.EnsureCreated()` in a project that also uses migrations | Medium | High | Very low | Later |
| EFD015 | `Contains()` over a large or unbounded in-memory collection in a SQL-backed query | Medium | Medium | Medium | Later |
| EFD024 | In-memory filtering of an `Include`d collection instead of a filtered `Include` | Medium | Medium | Medium | Later, advisory |
| EFD026 | `Update(entity)` marking every column modified when only some changed | Medium | Medium | Medium | Later, advisory |
| EFD035 | Model-snapshot hygiene: unbounded `nvarchar(max)` strings and `decimal` without precision | Low-Medium | High | Medium | Later, advisory |
| EFD036 | Column used in a query predicate with no covering index in the model snapshot | High | Medium | Medium | Later, advisory |
| EFD016 | Missing `CancellationToken` on an async EF call when one is in scope | Medium | High | Medium | Later, advisory |
| EFD007 | Read-only query that appears to need `AsNoTracking` | Medium | Low-Medium | High | Held back |
| EFD008 | Lazy loading or navigation access in a loop (N+1) | High | Low-Medium | High | Held back |

### Why this order

- **EFD028** prevents a runtime exception, with one precise, semantically resolved trigger. That's the shape that has worked best so far.
- **EFD030 and EFD031** are cheap and precise, but the shapes are less common.
- **EFD032** needs the analysis to follow query *results* through locals, not only queries.
- **EFD036** could be the highest-impact static finding. It maps predicate columns to snapshot indexes, which is real work.
- **EFD007 and EFD008** are valuable but depend on context the analyzer can't see, so they would be noisy. They stay held back until there's a precise formulation.

## Cross-cutting work

These improve existing rules rather than adding new ones:

- **Conditionally composed query locals.** Rules follow a query stored in a local only when its value is statically determined. The most common real-world shape, `if (x) query = query.Where(…)`, is not followed. Following it would raise recall for the rules that only need to know where a query comes from: EFD004, EFD009, EFD010, EFD013, EFD019, EFD022, and EFD023.
- **Raw-SQL coverage.** EFD012 covers `FromSqlRaw`, `ExecuteSqlRaw`, and `ExecuteSqlRawAsync`, but not EF Core 8's `Database.SqlQueryRaw<T>`.
- **Validation.** Keep growing the corpus, and triage every finding against the 90% precision bar.
- **Platforms.** Add Windows to continuous integration.

## Possibly later

- **Optional live SQL Server checks**, read-only and local:
  - implicit conversions that prevent index seeks;
  - missing-index evidence from `sys.dm_db_missing_index_details`;
  - Query Store evidence of highly variable query cost;
  - repeated high-cost queries.

  DMV suggestions would be presented as evidence, not automatic recommendations.
- **Editor integration**, if the CLI proves useful.

## Out of scope

- Automatic code fixes
- Dapper or raw ADO.NET analysis
- Query plan visualization
- Hosted services, source uploads, and telemetry
