# Validation findings: round 1

**Date:** 2026-09-26 · **EFDoctor:** `main` after the repository consistency checks · **Corpus:** 7 public projects (`corpus.json`)

## Corpus results

| Project | Kind | Complete | Findings | Notes |
|---|---|---|---:|---|
| eShop (dotnet) | Reference app | yes | 15 | All triaged |
| ardalis/CleanArchitecture | Template | yes | 1 | All triaged |
| jasontaylordev/CleanArchitecture | Template | **no** | – | Pins SDK 10.0.401 (`latestFeature`); this machine has 10.0.400 |
| Jellyfin | Application | yes | 62 | All triaged |
| Bitwarden | Application | yes | 80 | 16 triaged; 64 EFD005 deferred until the EFD005 fixes below |
| Smartstore | Application | yes | 196 | 58 triaged; 138 EFD005 deferred |
| OpenIddict (EF Core stores) | Library | yes | 7 | All triaged. Analyzable since `fix-cli-multiline-target-frameworks`; see below |

See `SUMMARY.md` for the per-rule precision tables. Headline numbers for fully triaged rules:

- **Strong:** EFD002, EFD011, and EFD017 are 100% on real code. EFD023 (advisory) is 78% strict and 100% detection.
- **Fixed:** EFD001, EFD006, EFD010, and EFD020 now have 0 FP on the corpus. EFD010 went from 79% to 100% detection precision. EFD020 went from 0% / 6% to a single finding, triaged `acceptable`. EFD001 was 3% / 23%, and after its fix it is 17% strict / 100% detection: 1 TP, 5 acceptable, 0 FP.
- **EFD005:** Defects 1–3 are fixed by `fix-efd005-key-equality-recognition`, and fluent and convention keys by `improve-efd005-model-key-sources`. There are 150 findings, down from 244. Detection precision on the 33 triaged findings is 100%. The 129 medium- and high-confidence findings still need triage (see the note under defect 3).

## Rule defects, in priority order

Each defect names the false-positive class, where it was seen, and a proposed fix.

### 1. EFD005 misses key equality on `Guid` and other structs (bug, fixed)

`x.Id == id` is not recognized as a key-equality bound when the key is a `Guid`. `Guid` defines its own `==` operator, and `ClassifyPredicateBound` requires `binary.OperatorMethod is null`, which holds only for built-in operators. Guid keys are extremely common.

- **Seen in:** eShop `IntegrationEventLogService` (high confidence), Jellyfin `UserDataManager`, `LinkedChildrenService` (high), and `PeopleRepository` ×2. It is also the likely cause of most of Bitwarden's 64 EFD005 findings.
- **Fix:** accept a user-defined `==` whose operand types are the same struct, which covers `Guid`, `DateTimeOffset`, and similar.

### 2. EFD005 misses key equality written as `.Equals()` (bug, fixed)

`e.ItemId.Equals(itemId)` is equality just like `==`, but only `==` is recognized.

- **Seen in:** Jellyfin `KeyframeRepository`, `TrickplayManager` ×2, and `DisplayPreferencesManager`. Jellyfin uses `.Equals()` throughout.
- **Fix:** treat a one-argument instance `Equals` between an entity key path and a non-entity value as `==`.

### 3. EFD005 reports per-parent loads that its own docs call legitimate (decided: advisory, fixed)

A key-by-name bound (`x.ParentId == parentId`) has Medium strength, and Medium is still reported at medium confidence. That covers two cases the rule page treats as fine:

- **Primary key** (`x.Id == id`): at most one row.
- **Foreign key:** "hydrating children for a known parent", which EFD005's rule page names as a legitimate full materialization.

**Decision (2026-09-26): report key-by-name equality bounds as advisory only.** Keep the finding, but lower its confidence to advisory (Info severity) instead of medium, so it no longer counts against the medium-confidence precision gate. Don't drop it entirely. Stronger bounds, such as `Take`, are unchanged. This decision, together with fixes 1 and 2, settles most of the 200 deferred EFD005 findings. All three shipped in the `fix-efd005-key-equality-recognition` change.

**Re-run after the fix:**

| Project | Advisory EFD005 findings | Still medium or high | Of those, no bound recognized |
|---|---:|---:|---:|
| eShop | 3 of 7 | 4 | 4 |
| Jellyfin | 11 of 35 | 24 | 24 |
| Bitwarden | 48 of 64 | 16 | 16 |
| Smartstore | 53 of 138 | 85 | 85 |

- The count of EFD005 findings did not change. These codebases configure keys with the fluent API rather than attributes, so Guid and `.Equals()` keys are recognized by name, not proven strong.
- All nine findings named in defects 1 and 2 are now advisory and are triaged `acceptable`.
- **Still to triage:** the remaining medium- and high-confidence findings (no recognized bound). These are the ones that matter for the precision gate.
- **Follow-up done:** the `improve-efd005-model-key-sources` change proves keys from fluent configuration in the same project and from EF's `<Navigation>Id` convention.
  - EFD005 findings fell from 244 to 150. 94 were removed and none were added.
  - Key-by-name findings fell from 108 to 15.
  - Every removed finding filtered on a foreign key, such as `OrganizationId`, `ProductVariantAttributeId`, or `ItemId`.
  - Analysis time was unchanged: Bitwarden took 51.6 s, and Smartstore 20.1 s.
  - The medium- and high-confidence findings are untouched: eShop 4, Jellyfin 24, Bitwarden 16, Smartstore 85. These still need triage.

### 4. EFD001 doesn't recognize batching (major false-positive class, fixed)

24 of 31 findings save once per batch, not once per entity. The batch shapes are:

- `foreach (var chunk in items.Chunk(100)) { …; SaveChanges(); }`
- pager loops: `while ((await pager.ReadNextPageAsync<T>()).Out(out var page)) { …; SaveChanges(); }`
- a guarded flush: `if (processed >= Limit) { SaveChanges(); ChangeTracker.Clear(); }`

These are the batching the rule's own remediation recommends.

- **Seen in:** Jellyfin (3 of 4), Smartstore (20 of 27).
- **Fix:** don't report when the loop's iteration variable is a collection (a chunk or page), or when the save is inside an `if` whose condition compares a counter against a limit. Keep reporting per-entity loops.
- **Fixed** by `improve-efd001-batch-saves`. EFD001 now skips four batch shapes:
  - a loop over chunks;
  - a page declared by the loop condition;
  - a page loaded in the loop body and iterated before the save;
  - a threshold flush.

  Re-run results:
  - Smartstore fell from 27 findings to 6. The TP and 5 `acceptable` documented exceptions (generated keys, a startup seeder, a deliberate per-item commit) remain.
  - Jellyfin fell from 4 findings to 0. Its fourth finding, which I had triaged as a per-item save, turned out to be a `for (offset += BatchSize)` loop that saves once per batch.
  - EFD001 went from 23% to 100% detection precision, with 0 FP.

### 5. EFD020 counts the wrong things as executions (major false-positive class, fixed)

16 of 17 findings are false positives, from three causes:

- **Subquery composition** (13, Jellyfin): the local is used as `ids.Contains(x.Id)` or `ids.Any(…)` inside another query's expression-tree lambda. EF translates this into a single SQL statement. **Fix:** don't count uses inside expression-tree lambdas.
- **Different queries sharing a base** (2, Smartstore): `baseQuery.FirstOrDefault(p1)` and `baseQuery.FirstOrDefault(p2)` are two different queries, not a re-execution. The same holds for a local that holds a `DbSet`. **Fix:** count only terminal operators applied to the local with no additional predicate or composition, and exclude locals whose value is a bare `DbSet`.
- **Mutually exclusive branches** (1, Smartstore): `async ? await q.ToDictionaryAsync() : q.ToDictionary()` runs only one arm. **Fix:** don't sum executions across the arms of the same conditional.
- **Fixed** by `improve-efd020-execution-counting`. Three changes:
  - Uses inside any enclosing expression-tree lambda are not counted.
  - Predicate terminals are not counted.
  - Conditional arms count as one path.

  A bare-`DbSet` exclusion turned out to be unnecessary, because the predicate rule covers both Smartstore shared-base cases.

  Re-run results:
  - EFD020 fell from 17 findings to 1: the `acceptable` `country.FirstOrDefault()` called twice.
  - Detection precision is now 100%, with 0 FP.

### 6. EFD010 reports the sync arm of an explicit sync/async switch (fixed)

Smartstore implements every data method once with a `bool async` flag: `async ? await x.FirstOrDefaultAsync() : x.FirstOrDefault()`. The sync call runs only when the caller asked for synchronous execution.

- **Seen in:** Smartstore, 5 of 7.
- **Fix:** don't report a sync call in one arm of a conditional whose other arm awaits its async counterpart.
- **Fixed** by `improve-efd010-sync-async-switch`. EFD010 now has 19 findings, down from 24: 5 TP and 14 `acceptable` (seeding and one-off migrations), with 0 FP. Detection precision is 100%, up from 79%.

### 7. EFD006 ignores an explicit `AsSingleQuery()` (fixed)

EFD006 treats `AsSplitQuery()` as an opt-out, but an explicit `.AsSingleQuery()` is an equally deliberate choice.

- **Seen in:** Jellyfin `UserManager`.
- **Fix:** treat any explicit query-splitting mode as an opt-out.
- **Fixed** by `improve-efd006-explicit-single-query`. This matches EF Core, which doesn't log `MultipleCollectionIncludeWarning` once a splitting mode is chosen. EFD006 now has 1 finding: Bitwarden `SecretRepository`, triaged `debatable`. It has 0 FP.

## Recall gap

### The shared chain proof rejects `AsTracking()`, `IgnoreQueryFilters()`, and `IgnoreAutoIncludes()`

`EfQueryOperationAnalysis.BoundNeutralEfMethods` allows `AsNoTracking` but not `AsTracking`, `IgnoreQueryFilters`, or `IgnoreAutoIncludes`. Any query that uses them breaks the origin proof, so EFD004, EFD005, and EFD019 stay silent.

- **Seen in:** OpenIddict's stores use `.AsTracking()` on most queries, so OpenIddict's "0 findings" understates reality. For example, `OpenIddictEntityFrameworkCoreApplicationStore` loads every authorization for an application, unpaged.
- **Fix:** add these methods to the element-preserving set, which `EfQueryOperationAnalysis.ElementPreservingEfMethods` already includes.
- **Fixed** by `improve-query-chain-tracking-modifiers`. The re-run surfaced 4 new EFD023 findings in Smartstore (`ImageOffloader`, `IgnoreQueryFilters().Where(x => x.Body.Contains(...))`), all `acceptable`.
- **Correction:** OpenIddict's "0 findings" had a different cause. MSBuildWorkspace can't parse OpenIddict's multi-line `TargetFrameworks` property, so the design-time build fails and every compilation has no references. EFDoctor then silently reported a clean result. See the product gap below.

### Queries stored in locals

Before `improve-query-chain-local-tracking`, every rule that proves a query chain stopped at a local, so `var query = …; query.ToList()` was invisible. The change follows a local whose value is statically determined: every write is a plain statement in the declaring block, and the read comes after them.

- **Re-run:** 6 new findings, all triaged. There were no false positives, and no other project changed.
  - **Jellyfin:** 4 EFD010, all TP. Each is a synchronous `Count()` on a query local inside an async method; one comes after a straight-line `query = query.Where(…)`.
  - **Smartstore:** 2 EFD005, both `acceptable`. Each fully loads a small lookup table (delivery times, scheduled tasks).
- **Why so few:** the dominant real-world shape is conditional composition, `if (x) query = query.Where(…)`, which is deliberately not followed. A rough scan of the corpus (non-test code) finds about 550 self-recompositions under an `if`/`else`, against about 180 straight-line ones. Jellyfin has 160 vs. 76, Smartstore 289 vs. 84, and OpenIddict 80 vs. 12.
- **Follow-up:** let rules that need only the query's origin follow conditionally composed locals, merging the possible values. Those rules are EFD004, EFD009, EFD010, EFD013, EFD019, EFD022, and EFD023, and their findings don't depend on which filters apply. Rules that report an absence, such as EFD005's missing bound and EFD014's missing ordering, should keep the exact rule.

## Product gap: unrestored targets produce a silent "clean" result

When EF Core packages aren't restored, EFDoctor can't resolve `DbSet`/`DbContext`, every rule stays silent, and the CLI reports **0 findings with exit code 0**. Validation hit this three times: eShop, whose solution restore aborted on a MAUI workload; OpenIddict's sandbox apps; and OpenIddict's EF Core store itself. That last project was restored, but its design-time build failed, which the harness's restore check could not see. A user who forgets to restore, or whose restore partly fails, gets a misleading clean result.

- **Fix:** when a project references EF Core but its compilation can't resolve `Microsoft.EntityFrameworkCore.DbContext`, report a warning naming the project. Consider a non-zero exit code when that affects every EF project.

### Fixed: silent clean result for unresolvable EF Core projects

The `add-cli-ef-resolution-check` change fixes this. A project that uses EF Core but can't resolve `DbContext` is no longer analyzed. Instead:
- the CLI writes an `EFDoctor warning` naming it to standard error;
- when no EF Core project resolves, the run exits with `2`;
- the harness marks such runs incomplete.

The re-run showed:
- **OpenIddict** now fails with exit `2` and names `OpenIddict.EntityFrameworkCore`. Before, it reported a clean result.
- **Smartstore** is marked incomplete. `Smartstore.Web` uses EF Core but was never analyzed on this machine, because its design-time build fails: `Smartstore.LightningCss.Native` has no CLI for `osx-arm64`. The other Smartstore projects are analyzed as before.

### Follow-up: MSBuildWorkspace and multi-line `TargetFrameworks`

MSBuildWorkspace can't load a project whose `TargetFrameworks` property contains line breaks, as OpenIddict's `Directory.Build.props` does. Roslyn's build host splits the raw value on `;` without trimming, so each inner build gets a `TargetFramework` that includes the whitespace.

**Fixed** by `fix-cli-multiline-target-frameworks`. The CLI now passes a `CustomAfterMicrosoftCommonCrossTargetingTargets` hook that normalizes `TargetFrameworks` in the outer evaluation. OpenIddict is now analyzed: 7 EFD001 findings, all `acceptable`. Every one is in a fallback revoke loop, used when bulk operations are disabled, that saves each entity inside a `try`/`catch` which resets the entity and continues. That is an intentional per-item partial-failure boundary. No EFD005 finding appeared for the unpaged loads mentioned under the recall gap. That is expected: they filter on the parent's key (`authorization.Application.Id.Equals(application.Id)`), which EFD005 treats as a per-parent load.

### Follow-up: stdout purity when the SDK can't be resolved

When no installed SDK satisfies a target's `global.json`, JSON mode printed the error envelope followed by the installed-SDK list, so JSON-mode stdout was no longer a single JSON document. The list didn't come from a child process, as first assumed: native hostfxr prints it to the process's own standard output when `hostfxr_resolve_sdk2` fails inside MSBuildLocator.

**Fixed** by `fix-cli-sdk-precheck-stdout`. When a `global.json` governs the target, the CLI first runs `dotnet --version` in the target's directory with its output captured, and reports the failure without calling MSBuildLocator. jt-cleanarch's run now yields a single JSON error envelope.

## What held up well

- **EFD017** (4 of 4 TP), **EFD002**, and **EFD011** produced no false positives on real code.
- **EFD023**'s advisory findings were all correct detections, and most were worth acting on. Bitwarden looks up Teams integrations and cipher folders by substring search inside JSON columns.
- **EFD012** correctly flagged every dynamic SQL string. All of them were trusted identifiers (table and database names), which is the documented suppression case.
- EFD010 found real sync-over-async calls in Microsoft's own eShop (`CatalogApi.DeleteItemById`, and `Find` in a loop in two integration-event handlers).
- **Performance:** analysis took 1–53 s per project, including Smartstore (1,800+ EF call sites) in 21 s. Nothing crashed.

## Next steps

1. ~~Fix the EFD005 bugs (1, 2) and make key-by-name bounds advisory (3).~~ Done. Next, triage the remaining medium- and high-confidence EFD005 findings.
2. Fix EFD001 (4) and EFD020 (5). These are the two rules furthest below the precision gate.
3. Fix EFD010 (6), EFD006 (7), the chain-proof recall gap, and the unrestored-target warning.
4. Add each false-positive shape above as a regression fixture while fixing it.
5. Install SDK 10.0.401 to add jasontaylordev/CleanArchitecture, and re-run the whole corpus after the fixes.
