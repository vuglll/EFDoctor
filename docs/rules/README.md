# Rule boundaries

Each rule's full contract is on its own page in this folder. This page summarizes how far each rule follows your code, and what it deliberately leaves alone.


## Query locals

Every rule that proves an EF Core query chain traces it from the operator it reports on back to a `DbSet<T>` or `DbContext.Set<TEntity>()`, through resolved `Queryable` and EF Core composition. That covers EFD004, EFD005, EFD006, EFD009, EFD010, EFD013, EFD014, EFD017, EFD019, EFD020, EFD022, EFD023, EFD025, and EFD029. The chain may pass through a local variable when the local's value at that point is **statically determined**:

- the local is declared with an initializer, and every other write to it is a plain `query = …;` statement directly in the same block;
- it is never written through `ref`/`out`, a `ref` alias, deconstruction, or compound assignment;
- the read is in a later statement of that block, and not inside a lambda or local function.

The value is then the latest earlier write, or the initializer, so the rule analyzes the query as if it were written inline. Evidence that prints the chain names the local, for example `Queryable.Where -> local 'query' -> Queryable.OrderBy`.

```csharp
// Followed: every write is a plain statement in this block, so the ordering is known at Skip.
var query = context.Orders.Where(order => order.Active);
query = query.OrderBy(order => order.CreatedUtc);
var page = query.Skip(20).ToList();

// Not followed at any read: the composition at ToList depends on the path taken.
var recent = context.Orders.Where(order => order.Active);
if (onlyRecent) recent = recent.Where(order => order.CreatedUtc > cutoff);
var all = recent.ToList();
```

A local reassigned inside an `if`, a loop, or any other nested block is not followed at any read. Queries stored in fields or properties, or passed through parameters, return values, or helpers, are not followed either. EFD006 and EFD025 report at an include, so when includes are split across a local, each finding is still reported once.

## EFD001 control-flow boundary

EFD001 uses semantic method resolution plus lexical executable scope. It detects EF Core `SaveChanges` and `SaveChangesAsync` invocations inside `for`, `foreach`, `await foreach`, `while`, and `do`/`while`, including calls nested beneath ordinary blocks such as `if` and `try`. Loops that save once per batch are not reported. A loop saves once per batch when it iterates chunks (`foreach (var chunk in ids.Chunk(100))`), when a page is declared by its condition (`while (pager.ReadNextPage(out var page))`), when a page is loaded and iterated in its body, or when the save is a threshold flush such as `if (processed >= BatchSize)`.

The first version is intentionally not interprocedural: it does not follow helper calls, prove whether branches execute, or treat a lambda, anonymous function, or local function declared in an outer loop as part of that loop. It does detect a save surrounded by a loop inside the deferred callable itself. See [`docs/rules/EFD001.md`](EFD001.md) for the complete rule guidance.

## EFD002 expression boundary

EFD002 semantically identifies `Queryable.Count` expressions proven to originate from an EF Core `DbSet`, plus EF Core `CountAsync`. It reports direct zero/one comparisons used only for existence and recommends the equivalent `Any` or `AnyAsync` form.

The first version intentionally does not follow counts stored in variables, infer providers for arbitrary `IQueryable<T>` values, or report exact-count comparisons. See [`docs/rules/EFD002.md`](EFD002.md) for supported forms, exclusions, remediation, and suppression examples.

## EFD003 model-snapshot boundary

EFD003 semantically analyzes generated `ModelSnapshot.BuildModel` source when the project references an allow-listed provider whose database does not automatically create foreign-key indexes: SQL Server or PostgreSQL (Npgsql). A foreign key is covered when an index, primary key, or alternate key on the same entity—or an applicable base entity—starts with the complete ordered foreign-key property sequence. The finding evidence names the detected provider.

The rule does not run for known auto-indexing providers such as MySQL through Pomelo, or for unknown providers. It also does not reconstruct models from arbitrary `OnModelCreating` control flow, inspect historical migration operations, execute target code, or query a live database. A checked-in snapshot can be stale, and indexes managed outside EF migrations are not visible. Regenerate or review migrations and verify externally managed indexes before acting or suppressing. See [`docs/rules/EFD003.md`](EFD003.md) for the complete coverage contract and limitations.

## EFD004 expression boundary

EFD004 semantically identifies `ToList`, `ToArray`, `ToListAsync`, and `ToArrayAsync` calls whose inline query source is proven to originate from an EF Core `DbSet`. It reports when the buffered result is consumed immediately by a conservatively supported `Where`, `Select`, `OrderBy`, `OrderByDescending`, `Skip`, or `Take` operation.

It follows the query source through [query locals](#query-locals) but intentionally does not follow materialized values through locals or general data flow. It excludes explicit client-evaluation boundaries and downstream expressions containing custom calls, computed members, dynamic behavior, mutation, or user-defined operators. Review semantics and generated SQL before moving composition, especially where small bounded sets, result reuse, provider limitations, or tracking behavior justify client processing. See [`docs/rules/EFD004.md`](EFD004.md) for the complete rule contract.

## EFD005 query-boundary definition

EFD005 semantically identifies `ToList` and `ToListAsync` calls whose inline source is proven to originate from an EF Core `DbSet`. Strong bounds include a resolved query-side `Queryable.Take`, local-collection membership via `Contains` or `Any`, and equality on a key proven from data annotations, from fluent `HasKey`/`HasAlternateKey`/`HasForeignKey`/unique `HasIndex` configuration in the same project, or from EF Core's `<Navigation>Id` foreign-key convention. The value compared against the key may be any single value external to the filtered entity, including a property of another object (for example `child.ParentId == parent.Id`); the comparand kind does not affect whether the bound is recognized. Comparing two properties of the filtered entity itself is not a key-equality bound. Equality may use `==`, a type's own `==` operator (such as `Guid`'s), or `.Equals()`. Key-name heuristics and time-window predicates lower confidence to Advisory rather than suppressing. Projection, ordering, `Distinct`, `Skip`, `Include`, and tracking modifiers do not establish a maximum row count by themselves.

Confidence reflects the recognized bound and best-effort call-site context: clearly hot unbounded paths can be High, unknown context defaults to Medium, and key-by-name equality and time-window intent are Advisory. Test projects are skipped by default; with `--include-test-projects` their findings are centrally downgraded to Advisory/Info without being suppressed. The rule follows [query locals](#query-locals) whose value is statically determined, but no other stored query values, and it doesn't cross client-evaluation boundaries, and it yields to EFD004 and EFD019 at the same materializer. If every row is not required, review stable server-side paging, chunking, or purpose-built aggregates; full materialization remains legitimate for cases such as hydrating children for known parents. See [`docs/rules/EFD005.md`](EFD005.md) for full boundaries, exceptions, and suppression guidance.

## EFD006 include-chain boundary

EFD006 semantically identifies inline EF Core query chains with at least two distinct root-level collection `Include` properties. It reports once at the second collection include, recognizes filtered selectors and static syntax, and suppresses the finding when the same inline chain explicitly uses `AsSplitQuery` or `AsSingleQuery`, the two choices EF Core itself treats as acknowledging the trade-off.

The first version does not count reference navigations, duplicate paths, or collection paths found only beneath one `ThenInclude` branch, and it follows stored query values only through [query locals](#query-locals). It does not inspect global query-splitting configuration. Review generated SQL and expected cardinalities before choosing `AsSplitQuery`, projection, or separate queries because additional queries introduce round-trip and consistency trade-offs. See [`docs/rules/EFD006.md`](EFD006.md) for the complete contract.

## EFD009 case-transform boundary

EFD009 semantically identifies the parameterless `string.ToLower()` or `string.ToUpper()` applied to a mapped string column path inside the predicate of `Where` or a predicate-taking `Queryable` or EF Core async terminal, on an inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()` whose entity type matches the predicate parameter. It reports when the transformed column is compared with a value that doesn't reference the entity, using `==`, `!=`, `string.Equals`, `Equals`, or `StartsWith`, anchored on the transformation call with medium confidence.

Transformed comparison values, column-to-column comparisons, `Contains`/`EndsWith`, `StringComparison` overloads, invariant and culture overloads, computed properties, nested lambdas, projections, and unproven sources are not reported. The remediation leads with the referenced provider's option—removing the transformation under SQL Server's case-insensitive collations, or `citext`, `EF.Functions.ILike`, or an expression index on PostgreSQL—and an existing index on the transformed expression is a reason to suppress. See [`docs/rules/EFD009.md`](EFD009.md) for the complete contract.

## EFD010 sync-database-call boundary

EFD010 semantically identifies a synchronous EF Core database call inside an `async` method or `async` lambda that has a direct EF `*Async` counterpart: a materializing or aggregating LINQ terminal (`ToList`, `ToArray`, `ToDictionary`, `First`/`Single`/`Last` and their `OrDefault` forms, `Count`, `LongCount`, `Any`, `All`, `Min`, `Max`, `Sum`, `Average`, `Contains`, `ElementAt`, `ElementAtOrDefault`) whose source is traced to `DbSet<T>` or `DbContext.Set<TEntity>()`, `DbContext.SaveChanges`, or `DbSet.Find`. It anchors on the call, names the suggested `*Async` replacement, and reports at medium confidence. It skips the synchronous arm of an explicit sync/async switch, meaning a `?:` or `if`/`else` whose other arm already awaits the same EF counterpart.

Calls in non-async methods, calls that already use the `*Async` counterpart, terminals over in-memory sequences or unproven `IQueryable<T>` sources, and synchronous calls inside a synchronous lambda nested in an async method are not reported. EFD010 is complementary to EFD011, which flags blocking on an async call rather than choosing the synchronous API; the two do not double-report. See [`docs/rules/EFD010.md`](EFD010.md) for the complete contract.

## EFD011 direct-blocking boundary

EFD011 semantically identifies direct `.Result`, parameterless `.Wait()`, and `.GetAwaiter().GetResult()` consumption of supported EF Core asynchronous query terminals, `DbContext.SaveChangesAsync`, and `DbSet.FindAsync`. It recognizes configured awaiters, reports the complete blocking expression, and uses high confidence because both the EF source and BCL blocking API are resolved symbols.

The first version does not follow tasks through locals or general data flow, report timeout/cancellation `Wait` overloads, or diagnose unrelated async methods. Prefer `await` and propagate async through callers; when a truly synchronous host boundary cannot change, isolate and document it rather than adding another blocking wrapper. See [`docs/rules/EFD011.md`](EFD011.md) for the complete contract.

## EFD012 raw-SQL boundary

EFD012 semantically identifies interpolated strings and non-constant concatenation passed as the SQL argument of EF Core `FromSqlRaw`, `ExecuteSqlRaw`, or `ExecuteSqlRawAsync`, including through a local assigned exactly once in the same straight-line block. It reports high confidence because the dynamic SQL shape is proven; it does not claim that an input is attacker-controlled.

Constant SQL, placeholder arguments, and interpolated-safe APIs such as `FromSqlInterpolated` and `ExecuteSqlInterpolatedAsync` are not reported, and the rule does not follow fields, parameters, helpers, or cross-method flow. Pass values as parameters; when SQL structure such as a column name must vary, select it from a fixed allow-list. See [`docs/rules/EFD012.md`](EFD012.md) for the complete contract.

## EFD013 bulk-operation boundary

EFD013 identifies a materialized EF query consumed by a `foreach` that only assigns uniform values to scalar properties, or only removes each entity, immediately followed by a save on the same context. It reports medium confidence at the materializer and suggests `ExecuteUpdate`/`ExecuteDelete` (or their async forms) when the target's EF Core version exposes them.

Loops that read the current entity, branch, call other code, touch navigations, or save elsewhere are not reported. Bulk operations bypass change tracking, interceptors, and in-memory state, so review those effects before switching. See [`docs/rules/EFD013.md`](EFD013.md) for the complete contract.

## EFD014 unordered-pagination boundary

EFD014 semantically identifies `Queryable.Skip`, optionally followed by `Take`, on an inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()` with no `OrderBy`, `OrderByDescending`, `ThenBy`, or `ThenByDescending` earlier in the chain. It reports once per paging expression, anchored on the outer `Take` when present, with high confidence and `Correctness` category. An ordering applied after the paging operator does not count.

A bare `Take` without `Skip` is deliberately not reported, because top-N reads with no order are often intentional. Any recognized ordering suppresses the finding without judging key uniqueness, and conditionally composed query locals, arbitrary `IQueryable<T>` parameters, in-memory sources, and look-alike methods are outside the rule. Add a stable ordering, ideally on a unique key or with a deterministic tie-breaker, before `Skip`. See [`docs/rules/EFD014.md`](EFD014.md) for the complete contract.

## EFD017 projection boundary

EFD017 semantically identifies resolved EF Core `Include` and `ThenInclude` calls in an inline chain traced to `DbSet<T>` or `DbContext.Set<TEntity>()` and followed by a resolved `Queryable.Select`. It reports once, at the `Select`, when every projected leaf is definitely non-entity: scalar, enum, nullable value, or `string` values, and anonymous objects, tuples, or object constructions built only from them. The evidence lists every include path.

The rule stays silent for identity projections, wrappers containing the source entity, direct navigation or collection results, and other reference-typed values it can't classify without the runtime EF model. It follows [query locals](#query-locals) but not helpers, custom operators, or materialization boundaries, and it ignores string-based includes. Remove the redundant include when the projection is all the caller needs; return the entity when populated navigations are required. See [`docs/rules/EFD017.md`](EFD017.md) for the complete contract.

## EFD018 discarded-task boundary

EFD018 semantically identifies EF Core asynchronous operations—queryable-extension terminals and bulk operations, `SaveChangesAsync`, `FindAsync`, `AddAsync`/`AddRangeAsync`, and relational `ExecuteSql*Async`—whose task is discarded as a standalone statement, through an explicit `_ =` assignment, or as the body of a void-returning lambda, including after `ConfigureAwait(...)` or null-conditional access. It reports the complete discarded expression with high confidence.

Tasks that are awaited, returned, stored, passed on, composed, or synchronously blocked are not reported; blocking remains EFD011's domain. The rule complements the compiler's CS4014 warning by also covering synchronous methods and explicit discards. Await the operation and propagate async; for intentional background work, use a separately scoped context and observe the task. See [`docs/rules/EFD018.md`](EFD018.md) for the complete contract.

## EFD019 materialize-then-reduce boundary

EFD019 semantically identifies `ToList`, `ToArray`, or awaited `ToListAsync`/`ToArrayAsync` materializers over a proven inline EF query whose buffered result is immediately reduced with `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, ordered `Last`/`LastOrDefault`, `Any`, `All`, `Count`, `LongCount`, `Sum`, `Min`, `Max`, `Average`, or the `List<T>.Count`/array `Length` property. Predicate and selector lambdas must use the same SQL-capable shapes EFD004 accepts. It also reports a materialized result that initializes a local when every read of the local is `Count`, `Length`, `Any()`, `Count()`, or `LongCount()`, outside lambdas and local functions. When a count is only compared for existence, the recommendation is `Any` instead of `Count`. The finding is anchored on the materializer, and EFD005 yields to it there.

Query-side reductions, explicit client boundaries, stored lists whose rows may be used, arbitrary or in-memory sources, unsupported lambdas and overloads, and unordered `Last` are not reported. Apply the reducer to the query, or use its EF Core async counterpart such as `CountAsync`. See [`docs/rules/EFD019.md`](EFD019.md) for the complete contract.

## EFD020 multiple-enumeration boundary

EFD020 reports an `IQueryable<T>` local whose initializer is a proven EF Core query traced to a `DbSet` and that is executed two or more times in the same method, through `foreach` or a materializing or aggregating terminal such as `ToList`, `Count`, `Any`, `First`, `Sum`, or their async variants. The finding is anchored on the local's declaration with medium confidence and states how many executions were observed. Non-terminal composition such as `Where` or `Select` is not an execution. Two other uses aren't executions either: a use inside another query's expression-tree lambda, such as `Where(x => ids.Contains(x.Id))`, which EF composes into the same SQL statement; and a predicate terminal such as `Any(x => …)`, which runs a different query. Executions in the two arms of the same conditional count as one path.

A query executed at most once, a materialized `List<T>` reused many times, a local reassigned after its declaration, unproven or in-memory sources, and queries passed to or returned from other methods are not reported. Materialize the query once and reuse the result; suppress with a reason when a deliberate re-query must observe changed data. See [`docs/rules/EFD020.md`](EFD020.md) for the complete contract.

## EFD021 static-DbContext boundary

EFD021 reports a `static` field whose type resolves to EF Core `DbContext` or a derived type, anchored on the field declaration with high confidence. A `DbContext` is not thread-safe and is meant to live for one unit of work, so a process-lifetime shared instance leads to concurrency exceptions, stale first-level-cache reads, and unbounded change-tracker growth.

Instance fields, `const` and compiler-synthesized fields, non-`DbContext` types, and look-alike types that don't derive from EF Core's `DbContext` are not reported. `static` auto-properties and DI-singleton lifetimes are out of scope for this version. Register the context as scoped, use `IDbContextFactory<T>`, or create it in a short `using` block. See [`docs/rules/EFD021.md`](EFD021.md) for the complete contract.

## EFD022 StringComparison boundary

EFD022 reports a `System.String` method call that takes a `StringComparison` argument—`Equals`, `StartsWith`, `EndsWith`, `Contains`, `IndexOf`, or static `string.Equals`/`string.Compare`—inside a lambda passed to a `Queryable` operator whose inline source traces to `DbSet<T>` or `DbContext.Set<TEntity>()`. EF Core has no SQL translation for these overloads, so the query throws at runtime; the finding is anchored on the string-method call with high confidence under the `Correctness` category.

Plain comparisons without `StringComparison`, in-memory `Enumerable` queries, unproven `IQueryable<T>` parameters, calls outside query predicates, and calls nested inside an in-memory `Enumerable` operator within a predicate are not reported. Use a translatable comparison and rely on the column collation for case sensitivity, or materialize deliberately and suppress with a reason. See [`docs/rules/EFD022.md`](EFD022.md) for the complete contract.

## EFD023 leading-wildcard boundary

EFD023 semantically identifies `string.Contains(string)` or `string.EndsWith(string)` on a mapped string column—directly or after `ToLower()`/`ToUpper()`—and EF Core `EF.Functions.Like` whose pattern provably starts with `%` or `_` (a constant, the leftmost part of a concatenation, or leading interpolated text), inside the predicate of `Where` or a predicate-taking `Queryable` or EF Core async terminal on an inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()`. It reports once per search call at advisory confidence and `Info` severity.

Prefix searches, `Like` patterns of unknown shape, `StringComparison` overloads (EFD022) and `char` overloads, collection `Contains(column)`, column-to-column searches, computed properties, nested lambdas, projections, and unproven sources are not reported. A case transformation compared with `==`, `Equals`, or `StartsWith` belongs to EFD009, so each shape gets exactly one finding. The remediation leads with SQL Server full-text search or a PostgreSQL `pg_trgm` trigram index, and suggests `StartsWith` or a reversed column where the semantics allow. See [`docs/rules/EFD023.md`](EFD023.md) for the complete contract.

## EFD025 redundant-include boundary

EFD025 semantically identifies resolved EF Core `Include`/`ThenInclude` chains in one inline query traced to `DbSet<T>` or `DbContext.Set<TEntity>()`. It reports a chain whose full navigation path duplicates an earlier chain, or is a strict prefix of a longer chain anywhere in the same query. Paths are compared by resolved property symbol. Constant string paths are compared only with other string paths. Findings use `Info` severity and high confidence, and the earliest occurrence of every longest path is never reported, so deleting every reported chain keeps the loaded navigations unchanged.

A repeated leading `Include` that branches into a different `ThenInclude` is required syntax and is not reported. Filtered includes, casts, non-constant strings, string-versus-expression pairs, includes split across helpers or conditionally reassigned locals, element-type-changing operators, and model `AutoInclude()` configuration are outside the rule. See [`docs/rules/EFD025.md`](EFD025.md) for the complete contract.

## EFD027 concurrent-context boundary

EFD027 semantically identifies EF Core asynchronous query terminals and bulk operations, `SaveChangesAsync`, and `FindAsync`, and resolves the `DbContext` each runs on. The context comes from the receiver, or from the query's proven `DbSet`/`Set<T>()` origin. It is compared only when it is a never-written local or parameter, a field or auto-property of `this` or a static one, or `this`. It reports an operation that starts while another operation on the same context is pending, with high confidence, `Warning` severity, and the `Reliability` category. That covers a later argument of `Task.WhenAll`/`Task.WhenAny`, an operation started before an earlier task local is referenced again, and an operation inside an `Enumerable.Select` selector, over a captured context, that reaches a task combinator.

Sequential awaits, separate context instances, contexts that can't be compared by symbol, task locals referenced before the next operation, operations only inside lambdas, and selectors that create their own context are outside the rule. So are synchronous calls while a task is pending, `Parallel.*`, `Task.Run`, and tasks collected in loops. See [`docs/rules/EFD027.md`](EFD027.md) for the complete contract.

## EFD029 replaced-ordering boundary

EFD029 semantically identifies a `Queryable.OrderBy` or `OrderByDescending` whose inline source reaches an earlier `OrderBy` or `OrderByDescending`, through any `ThenBy`/`ThenByDescending` plus only `Where`, EF Core include, tracking, query-filter, tag, and split-query operators, on a query traced to `DbSet<T>` or `DbContext.Set<TEntity>()`. It reports each replacing call once, with high confidence, `Warning` severity, and the `Correctness` category.

`Skip`, `Take`, `Distinct`, projections, and every other operator between the two orderings end the search, because they can give the first ordering a purpose. The stretch between the two orderings must be inline: an earlier ordering held in a local is a default being overridden, and it is not followed, although the query before the first ordering may pass through a local. In-memory ordering, unproven sources, and look-alike methods are outside the rule. See [`docs/rules/EFD029.md`](EFD029.md) for the complete contract.

## EFD037 entity-over-fetch boundary

EFD037 semantically identifies `ToList`, `ToArray`, and awaited `ToListAsync`/`ToArrayAsync` on a query traced to `DbSet<T>` or `DbContext.Set<TEntity>()` that returns the entity type without `Include`, when the materialized value initializes a local. It reports the materializer when every reference to that local in the method is a `foreach`, a one-parameter `Select`, an `Any`/`All`/`Count`/`Sum`/`Min`/`Max`/`Average` with a lambda, a count, or an index read, and every reference to an entity inside those is a read of a scalar property. The method must read at least one such property, at most half of them, and leave at least four unread. It reports with advisory confidence, `Info` severity, and the `Performance` category.

Any other use ends the analysis without a finding: an entity or the local returned, passed, stored, compared, or modified; a navigation, collection, computed member, or method used; operators that return entities, such as `Where` or `First`; a reassigned local; a projected query; and single-entity loads. Scalar properties are counted from the entity type, not from the EF Core model. See [`docs/rules/EFD037.md`](EFD037.md) for the complete contract.

## EFD038 stale-tracked-entity boundary

EFD038 semantically identifies EF Core `ExecuteUpdate`, `ExecuteUpdateAsync`, `ExecuteDelete`, and `ExecuteDeleteAsync` on a query traced to `DbSet<T>` or `DbContext.Set<TEntity>()`, and looks for a tracked load of the same entity type from the same `DbContext` instance in an earlier statement of an enclosing block. A tracked load is a local declaration initialized by `Find`/`FindAsync`, or by `First`, `Single`, `Last`, their `OrDefault` forms, `ToList`, `ToArray`, or an async form, on a query of the entity type without `AsNoTracking`. The same instance is proven by symbol, as in EFD027. It reports each bulk operation once, under the `Correctness` category: with high confidence and `Warning` severity when the method later references the loaded local, loads the type again from the same context, or saves after modifying a loaded entity, and with medium confidence otherwise.

No-tracking loads, filters that compare the same property with different constants, projections, other entity types, contexts that can't be compared by symbol, loads and bulk operations in sibling branches or inside lambdas, loads that don't initialize a local, and methods that call `ChangeTracker.Clear()`, `Entry(…).Reload()`, or detach an entity after the load are not reported. Beyond that constant check, the rule doesn't compare the bulk operation's filter with the loaded rows, and it doesn't follow a context across methods. See [`docs/rules/EFD038.md`](EFD038.md) for the complete contract.
