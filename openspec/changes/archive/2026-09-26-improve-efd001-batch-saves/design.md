# Design

## Context

`SaveChangesInLoopAnalyzer` finds the innermost enclosing loop with a syntax walk that stops at callable boundaries, and reports any resolved `DbContext` save inside it. It has no notion of what one iteration processes. The corpus false positives (see the proposal) all save once per batch.

## Goals / Non-Goals

**Goals:**
- Recognize the four batch shapes seen in the corpus with local, syntax-plus-semantic-model checks.
- Keep per-item saves reported.

**Non-Goals:**
- General data-flow analysis, such as proving that a helper method returns a page.
- Estimating batch size.
- Changing confidence. A reported EFD001 finding stays high confidence.

## Decisions

### Consider only the innermost loop

**Decision:** Every check applies to the loop that `FindEnclosingLoop` already returns.

**Why:** A per-item loop inside a chunk loop is exactly the per-item save EFD001 targets, so an outer batch loop must not exempt it. Looking only at the innermost loop gives that result naturally.

### Collection test

**Decision:** A type is a collection when it is an array, or implements `System.Collections.IEnumerable`, and is not `string`.

**Why:** This covers `T[]` from `Chunk`, `List<T>`, `IPagedList<T>`, and similar types.

**Consequence:** A `foreach` over a sequence of collections, such as `List<List<T>>`, counts as batching. That is the intended reading.

### Page from the condition

**Decision:** Inspect the `while`/`do` condition, or the `for` condition and declaration, for these forms, all resolved through the operation's `SemanticModel`:
- `DeclarationExpressionSyntax` (`out var page`);
- `SingleVariableDesignationSyntax` in patterns (`is { } page`, `is var page`);
- `AssignmentExpressionSyntax` whose left side is a local.

It counts when any of those locals has a collection type.

### Page from the body

**Decision:** This check applies only to `while`, `do`, and `for` loops. A `foreach` already has a per-item iteration variable, and `foreach (var order in orders) { var lines = …; foreach (var line in lines) …; Save(); }` is a per-order save.

It succeeds when a `foreach` inside the loop body ends before the save and iterates an identifier that resolves to a collection local declared inside the same loop body.

### Threshold flush

**Decision:** Walk the ancestors from the save up to the loop. For each `IfStatementSyntax` whose `Statement` (not its `else`) contains the save, search the condition for a `>=`, `>`, or `==` binary expression where one side is either of these:
- an identifier or `%` expression whose local is the target of `++`, `--`, `+=`, or `-=` somewhere within the loop node (so `for` incrementors count);
- a member access named `Count` or `Length`, or an invocation named `Count`.

**Why:** Requiring an incremented local keeps `if (item.Price > 100)` and `if (i > 0)` (a `foreach` variable is never incremented) reported. The corpus threshold uses `processedInPartition++` with `>= Limit`.

## Risks / Trade-offs

- **[Risk]** `if (list.Count > 0) Save();` inside a per-item loop would count as a threshold flush, so it would stop being reported. → This shape is rare. It is documented as an accepted false negative, and the rule stays high-precision.
- **[Risk]** `foreach (var group in items.GroupBy(...)) { …; Save(); }` counts as batching, because `IGrouping` is a collection. → Saving once per group is batching.
