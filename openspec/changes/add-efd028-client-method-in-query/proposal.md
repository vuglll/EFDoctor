# Proposal

## Why

A method of the application's own, such as `IsOverdue(order)` or `order.FullName()`, called inside a `Where` or `OrderBy` lambda of an EF Core query can't be translated to SQL. Since EF Core 3.0 turned off implicit client evaluation, the query throws "The LINQ expression … could not be translated" when it runs. The code compiles and reads naturally, so the failure surfaces only on that path at runtime. EFD028 is the last rule in the roadmap's "Next" tier: it prevents a runtime exception, and it has one precise, semantically resolved trigger.

## What Changes

- Add **EFD028, client-only method in a SQL-translated query**: `Correctness`, `Warning`, high confidence. It reports an invocation of a method declared in the analyzed project's source when all of these hold:
  - The invocation is inside the lambda argument of a SQL-translated `Queryable` operator: `Where`, `OrderBy`, `OrderByDescending`, `ThenBy`, `ThenByDescending`, or a predicate terminal. The predicate terminals are `Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, and their EF Core async forms.
  - The operator's source is proven to originate at an EF Core `DbSet`, including through followed and conditionally composed query locals.
  - The invocation's receiver or an argument reads a parameter of a lambda in that operator's expression. EF Core evaluates a call that reads no lambda parameter on the client, as a query parameter, and that doesn't throw.
- **Methods that EF Core or a well-known extension can translate are excluded:**
  - methods marked `[DbFunction]`, or named in a `HasDbFunction` call in the same compilation;
  - static methods declared on a `DbContext` type, and methods whose body only throws, which are the usual database-function stub shapes;
  - methods marked with an attribute that a query-rewriting library recognizes: `[Projectable]` (EntityFrameworkCore.Projectables), `[InjectLambda]` (NeinLinq), `[Computed]` (DelegateDecompiler), and `[Expandable]` (LinqKit).
- **Out of scope for the first version:**
  - `Select` and other projections, where EF Core may evaluate the final projection on the client;
  - methods from referenced assemblies;
  - unmapped properties;
  - delegate invocations.

## Capabilities

### New Capabilities

- `efd028-client-method-in-query`: detection, exclusions, finding contract, suppression, and test coverage for EFD028.

### Modified Capabilities

None. The CLI runs every shipped analyzer under the existing `cli-analysis` requirement.

## Impact

- **Code:**
  - a new `ClientMethodInQueryAnalyzer`, which uses `EfQueryOperationAnalysis.TryAnalyzeSource` with the origin proof;
  - registration in `WorkspaceAnalyzer` and `AnalyzerReleases.Unshipped.md`.
- **Tests:**
  - analyzer fixtures, at least ten positive and ten negative;
  - `tests/Fixtures/EFD028.Sample` with console and JSON end-to-end tests;
  - a case in `EFD005.TestSample`.
- **Docs:**
  - `docs/rules/EFD028.md`;
  - the rules README boundary section;
  - the README and `PACKAGE.md` rule tables;
  - the usage severity table and suppression guide;
  - the roadmap and development history;
  - the CHANGELOG.
- **Validation:** a corpus re-run, with every finding triaged.
