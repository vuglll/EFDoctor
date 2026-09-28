# Design

## Context

Several existing analyzers already prove that a query is SQL-backed. EFD004, EFD005, and EFD019 use `EfQueryOperationAnalysis.TryAnalyzeSource`, which follows a direct invocation chain through `Queryable` operators and bound-neutral EF operators (`Include`, tracking modes, tags, split mode) back to a `DbSet<T>` or `DbContext.Set<T>()` origin. `EfQueryOperationAnalysis` also provides:

- lambda recovery (`TryGetLambda`, `GetLambdaExpression`)
- operation unwrapping
- `IsSimpleMappedProperty`, which rejects expression-bodied and block-bodied getters

`EfProviderDetection.Detect` identifies a referenced SQL Server or Npgsql provider from marker types in the compilation. EFD003 already uses it. See `proposal.md` for motivation and the EFD009 and CLI deltas for observable behavior.

## Goals / Non-Goals

**Goals:**

- Report only a column that is provably mapped and case-transformed, inside a predicate that provably runs in SQL, and compared against a value that doesn't reference the entity. That is the shape where an untransformed column could use an index.
- Tailor the remediation to the referenced provider without reading configuration or connecting to a database.
- Reuse existing helpers without changing their behavior for other rules.

**Non-Goals:**

- Reading database collations or indexes. A function-based index on `LOWER(column)` is what makes this finding medium confidence rather than high.
- `ToLowerInvariant`, `ToUpperInvariant`, culture overloads, `Trim`, `Substring`, and other column functions.
- `Contains`, `EndsWith`, and leading-wildcard `LIKE`, which belong to EFD023.
- Predicates in nested lambdas, `OrderBy` keys, join keys, or projections.
- A code fix.

## Decisions

### Anchor on the predicate-taking invocation

Register a compilation-start invocation action after resolving `Queryable`, `DbSet<T>`, `DbContext`, and `EntityFrameworkQueryableExtensions`. Detect the provider once per compilation.

An invocation qualifies when its normalized target is one of:

- `Queryable` `Where`, `Any`, `All`, `Count`, `LongCount`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, or `LastOrDefault`
- an EF extension named `AnyAsync`, `AllAsync`, `CountAsync`, `LongCountAsync`, `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync`, or `LastOrDefaultAsync`

It must also have a single-parameter predicate lambda at ordinal 1. For `Where`, reject the overload whose lambda takes an index parameter. Its source must pass `TryAnalyzeSource`.

Starting at each `ToLower()` call and walking outward to find its query was rejected. It would need to find the enclosing lambda, the invocation that receives it, and that invocation's chain, which is the same work in reverse and harder to bound.

### Require the predicate parameter to be the origin entity type

`TryAnalyzeSource` accepts any `Queryable` operator, including `Select`. After a projection, a property on the lambda parameter may be a computed expression rather than a column. The predicate's parameter type must equal the origin's entity type, which is the type argument of the origin's `DbSet<T>`. Anything else is rejected. This keeps the rule to real columns without extending the chain walker.

### Walk only the predicate body, not nested lambdas

Walk the lambda body's operations and skip any nested `IAnonymousFunctionOperation` subtree. Nested lambdas have their own parameter, and SQL translation of subqueries is out of scope for the first version.

For each invocation, all of the following must hold:

- the normalized target is `string.ToLower` or `string.ToUpper`
- it takes no parameters
- its `Instance` is a column path

### Recognize a column path locally

A column path is a chain of property references, after unwrapping implicit conversions and parentheses, that ends at the predicate parameter. Every segment must be an instance property with a getter that passes `IsSimpleMappedProperty`, and the last segment must be `string`. Reference navigations such as `order.Customer.Email` are allowed.

The walk builds a display path such as `Order.Customer.Email` for evidence. A small local helper is used because the private `TryGetEntityProperty` returns only the outermost property and applies nullable unwrapping that `string` doesn't need. Leaving it private avoids changing its callers.

### Classify the immediate comparison context

After unwrapping conversions, parentheses, and argument wrappers around the transformed call, look at its parent:

- `IBinaryOperation` with `Equals` or `NotEquals`: the other operand is the comparison value.
- An invocation of static `string.Equals(string, string)`: the other argument is the comparison value.
- An invocation of instance `string.Equals(string)`: the transformed call is either the instance or the argument, and the other one is the comparison value.
- An invocation of `string.StartsWith(string)` with the transformed call as the instance: the argument is the comparison value.

Any other parent means no finding, including:

- `Contains` and `EndsWith`
- `StringComparison` overloads, which are EFD022's domain
- `string.Compare`
- a call argument
- a projection

The comparison value must contain no reference to the predicate parameter, so column-to-column comparisons are excluded.

A transformed column that is merely present anywhere in the predicate is not enough to report, because many such shapes, like `Contains`, are non-sargable for other reasons. Reporting on them would blur the rule's claim.

### Report on the transformation call with provider-aware text

The location is the `ToLower()`/`ToUpper()` invocation syntax, which covers `customer.Email.ToLower()`. The finding uses:

- `Performance` category
- `Warning` severity
- `medium` confidence

Evidence names:

- the origin, for example `context.Customers`
- the operator, for example `Queryable.Where` or `EntityFrameworkQueryableExtensions.FirstOrDefaultAsync`
- the column path
- the method
- the comparison kind (`==`, `!=`, `string.Equals`, `Equals`, `StartsWith`)
- the detected provider, when there is one

Impact says that wrapping the column in a function prevents an index seek on it and typically forces a scan, depending on indexes, collation, and data.

Remediation has a provider-specific lead and a common tail:

- **SQL Server lead:** under the default case-insensitive collation, remove the transformation and compare the column directly.
- **Npgsql lead:** use a case-insensitive collation or `citext`, `EF.Functions.ILike`, or an expression index on `lower(column)`.
- **No provider detected:** both options are given.
- **Common tail:** normalize the comparison value in C# rather than the column, and suppress with a reason when an expression index on the transformed column exists.

### Keep CLI integration centralized

Register the analyzer once in `WorkspaceAnalyzer` and add an `AnalyzerReleases.Unshipped.md` entry. Mapping, ordering, rendering, exit codes, suppression, privacy, and the test-project policy stay the same.

## Risks / Trade-offs

- **Some schemas index `LOWER(column)`, or use a case-sensitive collation where lowering is deliberate.** → Use medium confidence, name the expression-index exception in the remediation, and support standard suppression with a recorded reason.
- **The current SQL Server provider may translate `==` differently under certain collations.** → The rule claims only that the column is wrapped in a function, which holds for EF Core's `LOWER`/`UPPER` translation. It does not claim how much the query costs.
- **Inline-only chain proof misses queries built in locals or repositories.** → Same boundary as the other query rules. Documented, and reconsidered after real-project validation.
- **Pairing with EFD023 and EFD022 later.** → `Contains`/`EndsWith` and `StringComparison` overloads are excluded now so those rules can claim them without duplicate findings.
- **Provider detection finds more than one provider in the compilation.** → `Detect` returns a single result, preferring SQL Server over Npgsql, and marks MySQL as auto-indexing. When the result is ambiguous or MySQL, use the no-provider remediation that lists both options.

## Migration Plan

Ship EFD009 enabled by default, register it in the CLI, and add release metadata, the rule page, and README, PACKAGE.md, and CHANGELOG entries. To roll back, remove the registration, analyzer, tests, fixture, and docs. No persisted data, report field, dependency, or JSON schema changes.
