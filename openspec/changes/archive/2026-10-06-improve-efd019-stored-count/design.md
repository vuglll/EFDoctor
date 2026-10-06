# Design

## Context

`MaterializeThenReduceAnalyzer` runs on each materializer invocation. `TryGetEligibleReducer` looks at the parent of the materialized value: a `Count`/`Length` property, or an `Enumerable` reducer. Anything else, including a variable initializer, ends the analysis. `UnboundedQueryMaterializationAnalyzer` calls the same method and stays silent when EFD019 will report.

`CountUsedForExistenceAnalyzer` (EFD002) has a private classifier for the six comparisons of a count with a constant that only test existence.

EFD037 added the pattern this change needs for locals: find the local a materialized value initializes, then check every reference to it in the method against a closed list of uses.

## Goals / Non-Goals

**Goals:**

- Advice that doesn't lead to another finding.
- Report a stored list only when nothing in the method could need its rows.

**Non-Goals:**

- Stored lists reduced by element reducers (`First`), predicates, or aggregates. Those read rows, and replacing each read with a query changes the number of round trips.
- Fields, properties, and parameters.
- A code fix.

## Decisions

### Share the existence-comparison classifier

EFD002's classifier is split: the part that walks from a `Count` call through an `await` stays in EFD002, and the part that classifies "this value is compared with a constant for existence" becomes an internal static method that both analyzers call. EFD002's behavior is unchanged, and its tests cover the move.

### Existence recommendation for inline counts

For the count reducers (`Count()`, `LongCount()`, `List<T>.Count`, array `Length`), the analyzer classifies the reducer's own parent. When it is an existence comparison, the remediation names `Any` or `AnyAsync`, negated for the empty-set forms, and the evidence names the comparison. A `Count(predicate)` becomes `Any(predicate)`. The message and location don't change.

### Stored count-only local

When the materialized value initializes a local declared by a declaration statement, every reference to the local in the method is checked. The finding is reported only when there is at least one reference and each one is:

- the instance of `List<T>.Count` or array `Length`;
- the source of `Enumerable.Any`, `Count`, or `LongCount` with no other argument.

and none of them is inside a lambda or local function. A reassignment, a `foreach`, an index, an argument, a return, a predicate reducer, or any other use makes the rule silent, because the rows may be needed.

Each read is classified as an existence test (`Any()`, or a count in an existence comparison) or a count. If all are existence tests, the remediation recommends `Any`; otherwise `Count`. With more than one read, it says to run the query-side operator once and reuse the value, so that one query doesn't become several.

The message keeps its format, with a different tail: "…and then used only for its count through local 'products'". Confidence stays high: the closed list of uses proves that no row is read.

### EFD005 yields

`UnboundedQueryMaterializationAnalyzer` already asks EFD019 whether it reports the materializer. The stored-local check is exposed through the same internal entry point, so one materializer still gets one finding.

### EFD037 doesn't overlap

EFD037 needs at least one scalar property read, and a count-only local reads none.

## Risks / Trade-offs

- [A count read twice becomes two queries if the advice is applied carelessly] → The remediation says to compute once and reuse.
- [The rows are tracked on purpose, to be found later by `Find`] → Rare, and visible to the author; suppress with a reason.
