# Tasks

## 1. Implementation

- [x] 1.1 Recognize `SqlQueryRaw` on `RelationalDatabaseFacadeExtensions` in `UnsafeRawSqlAnalyzer`. Verify with new positive cases (direct interpolation, concatenation, an unsafe local, static form) and negative cases (constant SQL with parameters, `SqlQuery` with an interpolated string, a same-named non-EF method) in `UnsafeRawSqlAnalyzerTests`.

## 2. Docs and verification

- [x] 2.1 Update `docs/rules/EFD012.md`, the README and `PACKAGE.md` rule rows, the roadmap's cross-cutting list, and the CHANGELOG. Verify that `RepositoryConsistencyTests` pass.
- [x] 2.2 Re-run the corpus, triage any new EFD012 finding, and record the results in `validation/FINDINGS.md`. Run `openspec validate --strict` and the denylist grep, and verify that both are clean.
