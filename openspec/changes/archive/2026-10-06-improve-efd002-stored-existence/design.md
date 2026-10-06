# Design

## Context

`CountUsedForExistenceAnalyzer` runs on each invocation. For `Queryable.Count` over a query traced to a `DbSet`, or `EntityFrameworkQueryableExtensions.CountAsync`, it walks from the call through an `await`, parentheses, and implicit conversions, and reports when the value's parent is one of six comparisons with a constant. `TryClassifyExistenceComparison` is now a shared internal method (the EFD019 change).

EFD019 and EFD037 both follow a materialized value into a local and check every reference to it against a closed list. This change applies the same pattern to a scalar and to a single entity.

Every EFD002 finding has high confidence today, and the descriptor's message is a fixed sentence about counts.

## Goals / Non-Goals

**Goals:**

- Report all fourteen remaining methods of the issue's test file, and nothing where the stored value is needed for anything else.
- Keep the `FirstOrDefault` finding honest about its smaller saving.

**Non-Goals:**

- Counts or entities stored in fields, properties, or passed through helper methods.
- `SingleOrDefault`, which throws when more than one row matches, so `Any` isn't equivalent; `Find`; and `LastOrDefault`.
- Projected queries. `Select(p => p.Description).FirstOrDefault() != null` is `null` for a row whose column is `null`, so it isn't an existence test.

## Decisions

### A stored count is followed through one local

When the count value, after any `await`, initializes a local declared by a declaration statement, every reference to the local in the method is checked. EFD002 reports the count invocation when there is at least one reference, each is the operand of an existence comparison, and none is inside a lambda or local function or writes the local. One reference that uses the count as a number, such as returning it or comparing it with `5`, makes the rule silent: the count is needed.

Confidence stays high. The shape is the inline one with a name in between.

### `FirstOrDefault` as an existence test

The call must resolve to `Queryable.FirstOrDefault` or EF Core's `FirstOrDefaultAsync`, with no argument or one predicate. The default-value overloads are excluded. Its source must be a proven query chain whose element type is the `DbSet`'s entity type, which rules out projections; `EfQueryOperationAnalysis.TryAnalyzeSource` gives the origin for both the synchronous and the asynchronous form.

The result, after any `await`, must be used only for a null check:

- inline, as the operand of `== null`, `!= null`, `is null`, or `is not null`;
- or as the initializer of a local whose every reference is one of those checks, with at least one, none inside a lambda or local function, and no write.

A user-defined equality operator is accepted only when the compiler synthesized it, as for a record.

Anything else, such as `?.`, `??`, returning the entity, or reading a member, means the entity is used.

### Medium confidence for `FirstOrDefault`

Decided with the maintainer. `FirstOrDefault` already fetches at most one row, so the gain is that row's columns, its materialization, and its change-tracker entry, against an `EXISTS` query. The impact and remediation texts say so, and name the one reason to keep the load: wanting the entity tracked.

In a build, the shared diagnostic factory makes these findings suggestions. Count findings stay warnings.

### One descriptor, two message placeholders

The message format becomes "This EF Core {0} is used only to test existence and {1}". Count findings pass the words that reproduce today's message exactly. `FirstOrDefault` findings read "This EF Core FirstOrDefault result is used only to test existence and loads an entity where an existence query would do".

A second descriptor with the same ID was considered, to give the `FirstOrDefault` finding its own title. It would put two titles on one rule ID in reports and documentation, so the title stays "Count used only to test existence", and the rule page explains the second shape.

### The issue's test file as an acceptance test

The file is added to the analyzer tests as it was posted. One test runs EFD002 and EFD019 over it and checks, per method, the rule, the confidence, and whether the recommendation is `Any` or `Count`.

## Risks / Trade-offs

- [A tracked `FirstOrDefault` is loaded on purpose, so that a later `Find` hits the tracker] → Medium confidence, and the remediation names the case.
- [The title says "Count" on a `FirstOrDefault` finding] → The message and evidence name the real call.
