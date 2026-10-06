# Proposal

## Why

Issue #13 lists shapes where a query is materialized only to learn whether it has rows, or how many. EFD019 already reports them when written inline, with two gaps:

- For `ToList().Count > 0` it recommends `Count()`. Following that advice produces `Count() > 0`, which EFD002 then reports. The right fix is `Any()`.
- A list stored in a local is never reported, because the rows might be reused. But when every use of the local is its count, they aren't: `var products = db.Products.ToList(); return products.Count > 0;` buffers every row to answer a yes-or-no question. EFD005 reports that code only as an unbounded `ToList`, which doesn't say what is wrong.

## What Changes

- When a count reducer's result is compared only for existence (`> 0`, `!= 0`, `>= 1`, `== 0`, `<= 0`, `< 1`), EFD019 recommends `Any`/`AnyAsync`, or its negation, instead of `Count`.
- EFD019 reports a materialized result stored in a local when the local is declared once from the materializer, every read is `Count`, `Length`, `Any()`, `Count()`, or `LongCount()`, and it is never reassigned, enumerated, passed, returned, or captured.
- For such a local, the recommendation is `Any` when every read is an existence test, and `Count` otherwise.
- EFD005 yields to the new finding, as it does to the inline one.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd019-materialize-then-reduce`: adds the existence recommendation and the stored count-only local, and narrows the "stored list is never reported" boundary.

## Impact

Affects `MaterializeThenReduceAnalyzer`, the comparison classifier it now shares with `CountUsedForExistenceAnalyzer`, `UnboundedQueryMaterializationAnalyzer`'s yield, their tests, the EFD019 fixture and end-to-end test, and the EFD019 documentation. No new rule, and no change to the finding contract, the JSON schema, or exit codes.
