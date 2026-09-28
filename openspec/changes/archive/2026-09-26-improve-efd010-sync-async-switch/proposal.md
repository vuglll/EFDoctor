# Proposal

## Why

Round-1 corpus validation (`validation/FINDINGS.md`, defect 6) put EFD010 at 21% strict and 79% detection precision. Its only false-positive class is 5 of Smartstore's 7 findings. Smartstore implements each data method once, with a `bool async` flag:

```csharp
var setting = async
    ? await _set.FirstOrDefaultAsync(x => x.Name == key)
    : _set.FirstOrDefault(x => x.Name == key);
```

The synchronous call runs only when the caller asked for synchronous execution. The method already awaits the async counterpart on the async path, so there is nothing to fix.

## What Changes

- EFD010 does not report a synchronous call that sits in one arm of a conditional, meaning either a conditional expression or an `if`/`else`, when the other arm awaits the EF async counterpart it would suggest (for example `FirstOrDefaultAsync` for `FirstOrDefault`). The awaited call may use `.ConfigureAwait(...)`.
- A synchronous call is still reported when the other arm awaits anything else, and when it is not inside such a conditional.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd010-sync-db-call-in-async`: "Detect a synchronous EF Core database call in an async context" gains the explicit sync/async switch exclusion.

## Impact

- **Code:** `SyncDatabaseCallInAsyncAnalyzer` only.
- **Tests:** new fixtures. Every EFD010 scenario gets spec traits; this is the first time the capability is traced.
- **Docs:** the analyzer description, `docs/rules/EFD010.md`, the README EFD010 boundary, and the CHANGELOG.
- **Validation:** re-run Smartstore and eShop. The 5 switch false positives should disappear. The true positives and `acceptable` findings should remain.
