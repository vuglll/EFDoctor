# Proposal

## Why

Issue #13 comes with a test file of twenty existence and count shapes. After the EFD019 extension, fourteen of them are still not reported, and all fourteen store a result in a local before testing it:

- **A stored count compared later** (ten methods): `var n = db.Products.Count(); return n > 0;`. EFD002 reports the same comparison written inline, and its spec excludes the stored form only because the first version did no value tracking.
- **`FirstOrDefault` checked against `null`** (four methods): `var p = db.Products.FirstOrDefault(); return p is not null;`. The reporter found three of these in production code, each with the result in a local.

## What Changes

- EFD002 reports a `Count` or `CountAsync` whose result initializes a local, when every read of the local is an existence comparison. A local that is also used as a number is not reported.
- EFD002 reports a `FirstOrDefault` or `FirstOrDefaultAsync` on an entity query with no projection, when its result is only checked against `null`: inline, or through a local whose every read is a null check. These findings have medium confidence, because the saving is one row's columns and change tracking, not a scan.
- The issue's test file becomes an acceptance test: every method in it is reported by EFD002 or EFD019, with the right recommendation.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `efd002-count-existence`: follows a count through a local that is only compared for existence, and adds the null-checked `FirstOrDefault`.

## Impact

Affects `CountUsedForExistenceAnalyzer`, its tests, the EFD002 fixture and end-to-end test, and the EFD002 documentation. EFD002's message format gains two placeholders; the message for existing findings is unchanged. No new rule ID, and no change to the finding contract, the JSON schema, or exit codes.
