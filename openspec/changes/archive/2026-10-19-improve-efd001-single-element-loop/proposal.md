# Proposal

## Why

Functional C# libraries make an optional value enumerable with zero or one element, and code built on them writes "if found" as a loop:

```csharp
foreach (Playout playout in maybePlayout)
{
    dbContext.Playouts.Remove(playout);
    await dbContext.SaveChangesAsync(cancellationToken);
}
```

EFD001 reports the save as once per item. In validation round 2, ErsatzTV, which uses LanguageExt, had 23 such false positives, every EFD001 finding but its one retry loop.

## What Changes

- A `foreach` or `await foreach` over an option type is not a loop execution scope for EFD001. An option type is a generic type with one type parameter named `Option`, `Maybe`, or `Optional`.
- The analyzer looks past such a loop for an enclosing one, so a save inside an option loop nested in a real loop is still reported, as is a real loop inside an option loop.
- The retry-loop shape from round 2 stays out of scope.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd001-detection`: "Recognize supported loop execution scopes" excludes option loops, with two scenarios.

## Impact

- **Code:** `SaveChangesInLoopAnalyzer`.
- **Tests:** analyzer tests for `Option<T>`, `Maybe<T>`, a duck-typed `Optional<T>`, and both nestings.
- **Docs:** `docs/rules/EFD001.md` and the CHANGELOG.
