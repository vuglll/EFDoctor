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

## New rules: EFD027 and EFD029 (2026-09-29)

The corpus was re-run for the two rules added in the public repository. jasontaylordev/CleanArchitecture is still incomplete (its SDK pin); the other six projects completed.

- **EFD029** (`OrderBy` that discards an earlier ordering): 1 finding, **TP**. Smartstore `ProductBatchContext.BuildSpecAttributesQuery` sorts with `.OrderBy(x => x.ProductId).OrderBy(x => x.DisplayOrder)`, where `ThenBy` was meant. The impact is benign there, because the results are regrouped by `ProductId`, but the first ordering is dead code.
- **EFD027** (concurrent operations on one `DbContext`): 0 findings. The corpus has 90 non-test `Task.WhenAll`/`Task.WhenAny` call sites. The ones that could involve data access (Jellyfin `SearchManager`, Smartstore `LocalizedEntityService`) combine provider or cache tasks, not EF operations on a shared context. So the corpus doesn't exercise the rule's shapes, and its precision on real code is still unmeasured. A codebase that mixes a scoped `DbContext` with `Task.WhenAll` would be a useful addition to the corpus.

## Build-time cost of the analyzer package (2026-10-06)

The rules also ship as the `EFDoctor.Analyzers` package, so their cost inside a build matters. It was measured with the compiler's analyzer report (`-p:ReportAnalyzer=true`), with the analyzer assembly added to two corpus builds on the .NET 10 SDK.

| Build | Compilations | All analyzers | EFDoctor | Share | Most expensive rule |
|---|---:|---:|---:|---:|---|
| Jellyfin, `Jellyfin.Server.Implementations` and its dependencies | 11 | 19.96 s | 0.28 s | 1.4% | EFD027, 0.08 s |
| eShop, `Catalog.API` and its dependencies | 5 | 0.53 s | 0.02 s | 4.4% | EFD003, 0.009 s |

- No rule dominates. The most expensive rule in any single compilation took 0.02 s.
- EFD003 reads a whole model snapshot, and cost 0.009 s on eShop's PostgreSQL snapshot. It looks only at the snapshot's `BuildModel` method, and does nothing in a project with no snapshot or no supported provider.
- A project that doesn't reference EF Core costs under a millisecond per rule, because every rule returns at compilation start when EF Core types are missing.

## New rule: EFD037 (2026-10-06)

The corpus was re-run for EFD037, entities materialized into a local when the method reads only a few of their scalar properties. No other rule's findings changed.

- **15 findings, no false positive.** 8 are `TP` and 7 are `acceptable`, so detection precision is 100% and strict precision is 53%. The rule is advisory, so it doesn't count against the high- and medium-confidence gate.
- **Bitwarden, 1 TP.** `NotificationRepository.MarkNotificationsAsDeletedByTask` loads tracked `Notification` entities and reads `Id` and `UserId`, 2 of 11 scalar properties.
- **Smartstore, 7 TP.** The clearest is `LocalizedEntityService`, twice: a prefetch on a cached hot path loads 13 columns of `LocalizedProperty`, including the hidden flag and audit columns, and keeps `EntityId` and `LocaleValue`. The others load every `Country` or `CustomerRole` to build an Id map or option list, every resource of a language for an XML export, and external login records to build `UserLoginInfo`.
- **Smartstore, 7 acceptable.** Six are in installation seed data, and one is in a migration. The shape is right, but the code runs once over a few rows.
- eShop, Jellyfin, OpenIddict, and Ardalis's template have no finding. Their materialized entities are returned, mapped by a helper, or filtered in memory first, all of which the rule's closed list of uses leaves alone.

What the rule didn't see is the larger number. Results that pass through `Where`, `OrderBy`, or `First` before being read are common in the corpus, and following them is the obvious next step for recall.

## New rule: EFD038 (2026-10-06)

The corpus was re-run for EFD038, a bulk operation after a tracked load of the same entity type on the same context. The corpus uses bulk operations heavily: about 115 `ExecuteUpdate`/`ExecuteDelete` call sites each in Jellyfin, Bitwarden, and Smartstore, and 14 in OpenIddict.

**The first run found a false positive, which changed the rule.** A Smartstore migration (`20221028120000_PriceSettings`) loads the setting named `PriceSettings.DefaultRegularPriceLabelId` to check that it exists, bulk-deletes the setting named `CatalogSettings.PriceDisplayStyle`, and then saves unrelated additions. The draft rule reported it with high confidence, because `SaveChanges` followed the bulk operation. Two things were wrong, and both are fixed:

- **The filters can't match the same row.** The rule is now silent when the load and the bulk operation each compare the same property with a different constant.
- **A `SaveChanges` proves nothing by itself.** It writes stale state back only when a loaded entity was modified, so it now raises the confidence only then.

**After the fix: 3 findings, all in Bitwarden, no false positive.** 1 is `TP` and 2 are `acceptable`.

- **TP, high confidence.** `AccessPolicyRepository.UpdateProjectServiceAccountsAccessPoliciesAsync` loads a project's access policies with tracking, bulk-deletes a subset selected by the IDs of those same entities, and then passes the whole list, deleted entities included, to its upsert helper before `SaveChanges`. The sibling method `UpdateServiceAccountGrantedPoliciesAsync` does the same job with `RemoveRange`, which keeps the tracker in step, and is correctly not reported.
- **Acceptable, medium confidence, twice.** `OrganizationUserRepository.DeleteManyOrganizationUsersAndRelatedDataAsync` loads `CollectionUsers` and `OrganizationUsers`, reads them, and bulk-deletes them with the same filter. Nothing uses them afterwards, and the context ends with the method. This is what the medium tier is for.

Jellyfin, Smartstore, and OpenIddict have no finding: their bulk operations run without a tracked load of the same type in the same method.

One high-confidence finding is too few to measure the tier's precision. The known weak spot is a bulk filter that can't match the loaded rows for a reason other than different constants, such as loading active rows and deleting expired ones.

## EFD019 extension: stored count-only lists (2026-10-06)

The corpus was re-run after EFD019 learned to report a materialized result stored in a local that the method uses only for its count, and to recommend `Any` when a count only tests existence.

- **2 new findings, no false positive.** 1 `TP`, 1 `acceptable`.
- **Bitwarden, TP.** `ProjectRepository.ProjectsAreInOrganization` loads every matching `Project` entity and then compares `results.Count` with the number of requested IDs. `CountAsync` returns that number without loading a row.
- **Smartstore, acceptable.** A migration loads the settings with one name and only tests the list for emptiness, where `AnyAsync` would do. It runs once over a handful of rows. EFD005 used to report this materializer as unbounded; it now yields to EFD019, whose message says what is wrong.
- No existing EFD019 finding changed, and no inline finding in the corpus compares a count for existence, so the `Any` recommendation didn't change any remediation there.

## EFD002 extension: stored counts and null-checked `FirstOrDefault` (2026-10-06)

The corpus was re-run after EFD002 learned to follow a count through a local, and to report a `FirstOrDefault` whose result is only checked for `null`.

- **1 new finding, no false positive.** Smartstore's `InvariantSeedData.Menus` stores `_db.Manufacturers.Count()` in `manufacturerCount` and only compares it with `> 0`, twice. The shape is right; it is installation seed code, so the verdict is `acceptable`.
- **No null-checked `FirstOrDefault` in the corpus.** Where the corpus null-checks a `FirstOrDefault`, it goes on to use the entity, which the rule leaves alone. So the medium-confidence tier is unmeasured here. Its evidence is the proposal itself: the reporter found three such cases in production code, each with the result in a local.
- **The proposal's test file is now an acceptance test.** Its twenty methods each get exactly one finding: fourteen from EFD002 and six from EFD019.

## Conditionally composed query locals (2026-10-18)

The corpus was re-run after the rules that need only a query's origin (EFD004, EFD009, EFD010, EFD013, EFD019, EFD022, and EFD023) learned to follow a query local that branches recompose with `query = query.Where(…)`, and after the chain proof learned that `DbSet<T>.AsQueryable()` is the set.

- **No new finding from conditionally composed locals.** The recompositions in the corpus are mostly ID-list membership filters (`ids.Contains(x.Id)`), which none of these rules reports. Most of the rest are in `IQueryable` extension methods, whose source is a parameter that no rule can prove. The one text search among them (Smartstore's newsletter `Email.Contains`) runs over a joined projection, not an entity query. The added recall waits for code that has these rules' defects inside conditional composition.
- **5 new EFD005 findings from `DbSet<T>.AsQueryable()`, no false positive.** All five are in Smartstore: `_db.X.AsQueryable().Select(x => x.Id).ToListAsync()`. EF Core's own `AsQueryable` method used to end the proof, so these queries were skipped.
  - **1 `TP`:** the customer importer loads every customer number, from a table that grows with the shop, to check uniqueness.
  - **4 `acceptable`:** stores, affiliates, and permission records, all small lookup tables.
- No existing finding changed or disappeared.

## New rule: EFD028 (2026-10-18)

The corpus was re-run after EFD028 was added. It reports a method of the application that reads a query's lambda parameter inside a translated filter or ordering.

- **0 findings.** That is the expected result for released code. The shape throws "could not be translated" the first time the query runs, so it rarely survives into code that ships. The rule's value is catching the mistake during development, and in the IDE through `EFDoctor.Analyzers`.
- No other finding changed.
- The precision of the rule is therefore unmeasured on the corpus. The analyzer tests cover the exclusions that matter in practice: calls that read no lambda parameter, database-function mappings, and methods marked by query-rewriting libraries.

## EFD012 extension: `SqlQueryRaw<T>` (2026-10-18)

EFD012 now also checks the SQL passed to `Database.SqlQueryRaw<T>`. No project in the corpus calls `SqlQueryRaw`, so the extension can't change any corpus finding, and the corpus wasn't re-run for it. The analyzer tests cover the new positive and negative shapes.

## New rule: EFD030 (2026-10-18)

The corpus was re-run after EFD030 was added. It reports a deferred query returned from inside the `using` scope of its `DbContext`.

- **0 findings, which is correct.** Every corpus method that returns an `IQueryable`, such as Bitwarden's `IQuery<T>.Run(DatabaseContext)` classes, takes the context as a parameter, so the caller owns its lifetime. The one `IAsyncEnumerable` method that opens a scope is an async iterator, which keeps the scope open during enumeration.
- No other finding changed.
- Bitwarden's repositories resolve the context through a helper, `GetDatabaseContext(scope)`, which EFD030 doesn't follow. Those methods materialize before returning anyway.

## New rule: EFD032 (2026-10-18)

The corpus was re-run after EFD032 was added. It reports a change to an `AsNoTracking` entity before `SaveChanges`.

- **0 findings.** The corpus uses `AsNoTracking` only for reads (Smartstore's measure lookups, Bitwarden's read paths), and loads with tracking when it updates. Like EFD028 and EFD030, the shape is a bug that tends to be fixed before release, so the rule's value is in development and in the IDE.
- No other finding changed.

## New rule: EFD033 (2026-10-18)

The corpus was re-run after EFD033 was added. It reports a singleton or hosted service that captures a scoped `DbContext`.

- **0 findings.** The corpus's singletons and hosted services use the correct patterns. Bitwarden's singleton repositories take `IServiceScopeFactory` and open a scope per call. Jellyfin's hosted services take managers and providers, not a context.
- **One transitive shape is outside the rule.** eShop's `GracePeriodManagerService`, a hosted service, takes `IGracePeriodOrdersRepository`, which could depend on the context. Following registrations transitively is a non-goal of the first version.
- No other finding changed.

## New rule: EFD034 (2026-10-18)

The corpus was re-run after EFD034 was added. It reports `EnsureCreated` on a context that uses migrations.

- **The first run found 1 finding, and it led to a refinement.** ardalis-cleanarch's `MigrateDatabaseAsync` calls `EnsureCreatedAsync()` when `context.Database.IsSqlite()`, and `MigrateAsync()` in the `else` branch. The rule found the shape correctly, but the switch is deliberate: the two calls never meet on one database. The finding would have been `acceptable`, which lowers strict precision for a high-confidence rule.
- **Refinement:** EFD034 no longer reports `EnsureCreated` in one arm of a conditional when the other arm migrates the same context type, the same exclusion EFD010 makes for its sync/async switches. After it, **0 findings**.
- No other finding changed.

## New rule: EFD031 (2026-10-18)

The corpus was re-run after EFD031 was added. It reports LINQ composed over a stored-procedure call in `FromSqlRaw`.

- **0 findings, which is correct.** The corpus has one stored-procedure query: Bitwarden's `OrganizationMemberBaseDetailRepository` calls `FromSqlRaw("EXEC [dbo].[MemberAccessReport_…] @OrganizationId", …)` and materializes it directly with `ToListAsync()`, which EF Core supports. The rule leaves it alone, as designed. The corpus's other four `FromSql` calls use composable `SELECT` text.
- No other finding changed.

## New rule: EFD036 (2026-10-18)

The corpus was re-run after EFD036 was added. It reports a query filter on columns that no snapshot index leads.

- **0 findings, measured on two services.** eShop's Catalog.API and Webhooks.API keep their model snapshot in the same project as their queries, so EFD036 joins them. Every filter they apply is led by an index or key: `Name` (`StartsWith`), `CatalogTypeId` and `CatalogBrandId` (`Contains`), `UserId`, `Type`, and `Id`. `Embedding != null` isn't a seekable shape. These are correct true negatives.
- **Elsewhere the layout is outside the rule.** Bitwarden, Jellyfin, eShop's Ordering service, and ardalis-cleanarch keep their snapshot in a separate migrations or infrastructure project from most of their queries. Smartstore and OpenIddict have no EF snapshot. A snapshot in another assembly can't be read, which is a documented boundary.
- No other finding changed.

## New rule: EFD035, and a fix to EFD036 (2026-10-18)

The corpus was re-run after EFD035 was added. It reports an indexed `nvarchar(max)` and a `decimal` left at `decimal(18,2)` in the model snapshot.

- **The first run found a bug, in EFD036 as well.** EF Core writes every model snapshot with an `// <auto-generated />` header, and both snapshot rules skipped generated code, so EFD035 found nothing in Bitwarden's SQL Server snapshot. EFD036, merged earlier, had the same flaw: it could never read a real snapshot. Both fixtures had used hand-written snapshots without the header. Both rules now analyze generated code. EFD036 still skips queries in generated code. The fixtures and tests now use generated snapshots.
- **3 EFD035 findings after the fix, no false positive.** All are in Bitwarden's `SqlServerEFScaffold` snapshot, at advisory confidence.
  - **1 `TP`:** `TaxRate.Rate` is `DECIMAL(6,3)` in Bitwarden's hand-written SQL schema, but the EF model leaves it at `decimal(18,2)`, so EF-migrated databases round three-decimal tax rates.
  - **2 `acceptable`:** `Transaction.Amount` and `RefundedAmount` are currency amounts, where two decimal places lose nothing.
- **No indexed `nvarchar(max)` in the corpus.** Every indexed string has a maximum length.
- **EFD036 still has 0 findings after the fix.** The analysis above still holds: eShop's co-located snapshots cover every filter.
- No other finding changed.

## New rule: EFD015 (2026-10-18)

The corpus was re-run after EFD015 was added. It reports a key list materialized from one query only to filter another with `Contains`.

- **0 findings.** The corpus's materialized ID lists are used for more than one filter. Smartstore's `CustomerController` loads `allCustomerRoleIds` and passes it to a validator, then iterates it. Lists like that have to be materialized, and the rule leaves them alone by design. The corpus's `Contains` filters over composed queries (eShop's catalog filters, Smartstore's ACL subqueries) are already subqueries.
- No other finding changed.

## New rule: EFD024 (2026-10-18)

The corpus was re-run after EFD024 was added. It reports an `Include`d collection that is only used filtered in memory.

- **2 findings, both `TP`.**
  - **Bitwarden `UserRepository.GetPremiumAccessByIdsAsync`** includes every `OrganizationUser`, with its `Organization`, for each user, and only evaluates `OrganizationUsers.Any(enabled organization that grants premium)`. A filtered `Include`, or a projection that runs the `Any` in SQL, loads only what matters.
  - **Smartstore `TargetGroupEvaluatorTask`** includes every `RuleSet`, with its `Rules`, of each active role, and iterates only `role.RuleSets.Where(x => x.IsActive)`. A filtered `Include` would skip the inactive rule sets and their rules.
- No other finding changed.

## New rule: EFD026 (2026-10-18)

The corpus was re-run after EFD026 was added. It reports `Update` on an entity the method loaded with tracking.

- **0 findings.** Most `Update` calls in the corpus are on detached entities, `Update`'s real use.
- **One shape is just outside the first version.** Bitwarden's collection-access repository methods load `existingCollectionUsers` with tracking, pick an element in memory with `FirstOrDefault(…)`, change it, and call `dbContext.CollectionUsers.Update(existingCollectionUser)`. That `Update` is unnecessary too. EFD026 follows only direct EF loads, so in-memory lookups over a tracked list are a candidate extension.
- No other finding changed.

## New rule: EFD016 (2026-10-18)

The corpus was re-run after EFD016 was added. It reports an EF Core async call that leaves out the `CancellationToken` in scope.

- **3 findings, all in Smartstore, no false positive.** 1 `TP` and 2 `acceptable`.
  - **TP:** `SearchQueryAliasHook`'s duplicate check passes `(all, e) => all.AnyAsync(…)` without the hook's `cancelToken`, which the lambda could capture. The hook runs on every save of those entities.
  - **Acceptable:** the `InternationalDiallingCodes` migration's data method takes a token, and ignores it in `ToListAsync` and `SaveChangesAsync`. The shape is correct, but a one-time migration barely benefits from cancellation.
- The corpus otherwise passes its tokens consistently, which is why the count is low.
- No other finding changed.

## Validation round 2 (2026-10-19)

**EFDoctor:** `dev` at `502c52a`. **Corpus:** 10 projects. Three applications are new: Kavita, ErsatzTV, and Skoruba Duende IdentityServer Admin. Every finding in the corpus is triaged; none is left `TODO`.

### EFD005 backlog

The 115 deferred EFD005 findings in Bitwarden and Smartstore are triaged: 5 `TP`, 109 `acceptable`, and 1 `debatable`.

- **TP:**
  - Bitwarden: `GetManyByEnabledAsync`, `GetManyByPremiumAsync`, and `DeleteExpiredAsync` load whole filtered tables.
  - Smartstore: `CatalogSearchQueryAliasMapper` and `ByRegionTaxProvider`.
- **Acceptable:** mostly small lookup tables (settings, countries, stores, currencies), per-parent child loads, and admin lists.

### New projects

| Project | Findings | TP | FP | acceptable | debatable |
|---|---:|---:|---:|---:|---:|
| Kavita (`Kavita.sln`) | 211 | 29 | 8 | 158 | 16 |
| ErsatzTV (`ErsatzTV.sln`) | 175 | 12 | 83 | 78 | 2 |
| Skoruba Admin | 33 | 4 | 0 | 26 | 3 |

The new projects found two CLI bugs, both now fixed:

- **Test-project detection matched `xunit.assert`** (fixed by `fix-test-framework-detection`). `Kavita.Common` references `xunit.assert` for guard helpers. Every project that references it, including the EF Core data project, was classified as a test project and silently skipped, so Kavita first reported 0 findings. Detection now keys on a framework's core assembly.
- **Entity-model projects got a false "could not be resolved" warning** (fixed by `fix-ef-abstractions-only-project`). `Kavita.Models` references only `Microsoft.EntityFrameworkCore.Abstractions`, for `[Index]`, and has no `DbContext`. The CLI told the user to restore and build it, and the corpus marked the run incomplete.

### New false-positive classes, in priority order

1. **EFD006 ignores a global `UseQuerySplittingBehavior(SplitQuery)` (fixed). This causes 65 of EFD006's 68 findings.** ErsatzTV (59) sets it on every context registration, in `Startup` and the scanner's `Program`. Kavita (6) sets it in `ApplicationServiceExtensions`, in the same project as its repositories. The rule documents this limitation, but at this volume the rule is unusable on such codebases.
   - **Proposed fix:** treat a `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)` call in the same compilation as an opt-out, which covers Kavita.
   - ErsatzTV configures it in another project. The analyzer can't see that, so the CLI would have to pass a solution-wide fact.
2. **EFD001 treats a `foreach` over a LanguageExt `Option<T>` as a loop (23 FP, all ErsatzTV; fixed).** `foreach (var x in maybeX)` runs zero or one time, and ErsatzTV writes every "if found" branch this way. One more FP is a three-attempt retry loop around `SaveChangesAsync`.
   - **Proposed fix:** don't treat a loop over an option type as repetition. The candidates are LanguageExt's `Option<T>`, and other single-element enumerables recognized by name.
   - A `while` retry loop, bounded by an attempts counter and exited on success, is a second, smaller shape.
3. **EFD036 judges each predicate alone (1 FP, fixed).** In `Where(s => s.SeriesId == id).AnyAsync(s => s.ValidUntilUtc >= now)`, the indexed `SeriesId` filter is in a different call from the reported predicate.
   - **Proposed fix:** combine the top-level `&&` terms of every filter in the chain.
   - The same issue also reports two `Where` calls on one query as two findings.
4. **EFD024 doesn't recognize complementary filters (1 FP, fixed).** Kavita filters `ExternalRecommendations` in memory twice, with `SeriesId == null` and with `SeriesId != null`, so every row is needed.
   - **Proposed fix:** report only when every use applies the same predicate, or when there is a single use.

### Recall gap: AutoMapper `ProjectTo`

Kavita ends 116 of its 254 `ToListAsync` chains in AutoMapper's `ProjectTo<T>(…)`. The chain proof stops at it, so:

- EFD005 and the other chain rules don't see those queries;
- EFD006 misses an `AsSplitQuery()` written after `ProjectTo` (Kavita's `LibraryRepository.GetLibraryDtoByIdAsync`).

Treating `ProjectTo` as a shape-preserving projection would recover them.

### What held up well

- **EFD027** found a real concurrency bug. Kavita's `ReadingSessionRepository` starts three `ToDictionaryAsync` calls on one context and awaits them with `Task.WhenAll`, and EF Core throws on the second.
- **EFD017** (9 of 9), **EFD025** (7 of 7), and **EFD026** (2 of 2) had no false positives. **EFD037** went to 12 TP out of 19.
- **EFD005** stays at 100% detection precision over 376 findings, with 0 FP. Strict precision is low (15 TP, 4%) because most loads are small lookup tables, which is why the rule is advisory. Its clearest TPs load every row to filter or group in memory, for example:
  - Skoruba's persisted grants joined to every user;
  - Kavita's `ConsolidateProgress`.
- **Performance:** analysis takes 4–63 s per project. ErsatzTV is the slowest, at 63 s. Nothing crashed.

### Fixes after round 2

The corpus was re-run after each of four fixes, and nothing else changed:

- **`improve-efd006-global-split-query`.** EFD006 honors a split-query default configured in the same project. The CLI also honors one configured in any project it scans, and so does the new `efdoctor.split_query_by_default` global option. EFD006 falls from 68 findings to 3: Skoruba's 2 `acceptable` and Bitwarden's 1 `debatable`. It now has 0 FP.
- **`improve-efd001-single-element-loop`.** EFD001 doesn't treat a `foreach` over `Option<T>`, `Maybe<T>`, or `Optional<T>` as a loop. ErsatzTV's EFD001 findings fall from 24 to 1, the retry loop. EFD001 is now 1 TP, 16 `acceptable`, and 1 FP.
- **`improve-efd036-chain-filters`.** EFD036 judges a query on the filters of all its predicates, and reports it once. Kavita's EFD036 findings fall from 27 to 21:
  - one duplicate (`GetAllCollectionsForSyncing`) is gone;
  - five queries with an indexed filter in another `Where` are gone. These are `SeriesId` in `ExternalSeriesMetadataRepository`, and `LibraryId` in four `SeriesRepository` lookups.
  - Round 2 had triaged those four as 1 `TP` (`LowestFolderPath`) and 3 `acceptable` (`Format`), without noticing the earlier `LibraryId` filter. They were the same false-positive class.
- **`improve-efd024-complementary-filters`.** EFD024 skips a navigation filtered with complementary predicates (`e` and `!e`, `==` and `!=`, `<` and `>=`). Kavita's `GetSeriesDetailPlusDto` FP is gone, and the Bitwarden and Smartstore TPs remain. The round-2 proposal, which reported only identical predicates, was not used: it would also drop real findings such as `Any(p => p.Published)` together with `FirstOrDefault(p => p.Pinned)`.

### Next steps

1. Follow AutoMapper `ProjectTo` in the chain proof.
2. Consider EFD001's bounded retry loop.
