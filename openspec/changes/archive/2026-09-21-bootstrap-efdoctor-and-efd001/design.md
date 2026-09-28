# Design

## Context

The repository currently contains product documentation and OpenSpec configuration but no solution or implementation. See `proposal.md` for motivation and the three capability specs for behavioral contracts. The first slice must establish reusable boundaries without committing to packaging, additional rules, live SQL analysis, or remote services.

The CLI and tests target .NET 10. The analyzer targets `netstandard2.0` so Roslyn can load it in analyzer-compatible hosts independently of the CLI runtime. Runtime analysis assumes the requested project or solution is already restorable/buildable with locally available SDKs and dependencies; EFDoctor does not invoke package restore or add network behavior.

## Goals / Non-Goals

**Goals:**

- Establish project boundaries that let future rules reuse finding, reporting, and CLI orchestration code without coupling them to EFD001.
- Make semantic matching and executable-scope boundaries explicit and independently testable.
- Preserve Roslyn's native diagnostic configuration and suppression pipeline.
- Keep console and JSON rendering deterministic, testable, and free of hidden network or telemetry behavior.
- Make a clean checkout build and test with a small, documented command set.

**Non-Goals:**

- Designing analyzer packaging for IDE installation or NuGet/global-tool publication.
- Interprocedural or runtime control-flow proof, cost estimation, database connectivity, or code fixes.
- A generic plugin architecture for rules before a second rule demonstrates the required abstraction.
- Compatibility targets beyond the analyzer's `netstandard2.0` host compatibility and the .NET 10 CLI/test runtime.

## Decisions

### 1. Bootstrap a layered solution

Use this initial layout:

```text
EFDoctor.sln
Directory.Build.props
Directory.Packages.props
src/
  EFDoctor.Analyzers/          # netstandard2.0 DiagnosticAnalyzer and EFD001 metadata
  EFDoctor.Core/               # net10.0 finding model, ordering, and report contracts
  EFDoctor.Cli/                # net10.0 command parsing, workspace loading, mapping, renderers
tests/
  EFDoctor.Analyzers.Tests/    # net10.0 focused analyzer tests
  EFDoctor.Cli.Tests/          # net10.0 reporting and process-level integration tests
  Fixtures/
    EFD001.Sample/             # buildable project used by end-to-end tests
```

Central build and package settings keep nullable analysis, warnings, language settings, and dependency versions consistent. The analyzer remains standalone: it emits Roslyn diagnostics and diagnostic properties, while the CLI maps those diagnostics into `EFDoctor.Core` findings. This avoids making the analyzer depend on a .NET 10 runtime assembly and preserves a clean future analyzer-packaging path.

Alternative considered: target every project at .NET 10 and directly share the finding model with the analyzer. Rejected because analyzer hosts commonly require a more conservative target and analyzer dependency loading would become part of the first slice.

### 2. Analyze invocation operations, then apply semantic identity checks

Register EFD001 as a Roslyn `DiagnosticAnalyzer` operation action for invocation operations. Resolve `Microsoft.EntityFrameworkCore.DbContext` once per compilation by metadata name; if it is unavailable, the rule produces no diagnostics for that compilation.

For each invocation whose target name is `SaveChanges` or `SaveChangesAsync`, inspect the resolved `IMethodSymbol`. A method qualifies when the symbol itself or its `OverriddenMethod` chain corresponds to an instance save method on EF Core `DbContext`; inherited calls naturally resolve to the base method, while custom overrides are recognized by walking the override chain. Symbol equality uses Roslyn's symbol comparer rather than names or display strings.

Alternative considered: syntax-only matching. Rejected because it cannot distinguish unrelated same-name methods or reliably support subclasses and overrides. Alternative considered: match any method on a type assignable to `DbContext`. Rejected because a subclass may declare an unrelated overload with the same name; the base/override method chain is the stronger identity signal.

### 3. Define loop scope structurally with callable boundaries

After a method match, walk outward from the invocation syntax. A `for`, `foreach` (including `await foreach`), `while`, or `do` ancestor establishes loop execution. Ordinary statements and blocks do not interrupt the walk. Stop without using an outer loop when crossing a lambda, anonymous method, local function, or separate member boundary.

This intentionally uses lexical executable scope rather than whole-program control-flow inference. It catches invocations nested under `if`, `try`, `using`, switch, and similar blocks, but does not follow helper-method calls or prove whether a branch or loop executes. It also does not infer that a deferred callable declared inside a loop executes once per iteration, even if later invoked there; a loop inside that callable is still detected. These limitations favor explainable high-confidence results and must be documented.

Alternative considered: control-flow graph and interprocedural analysis. Deferred because it increases complexity and false-positive risk without being necessary to validate the first rule architecture.

### 4. Emit one Roslyn diagnostic per matching invocation

Place the diagnostic on the complete invocation expression. Configure EFD001 as enabled by default with warning severity and high-confidence metadata. Attach stable diagnostic properties for evidence, likely impact, remediation, confidence, and documentation key so the CLI does not reconstruct rule guidance from message text.

Roslyn remains the suppression authority. The CLI requests effective, non-suppressed analyzer diagnostics from each compilation and preserves project analyzer configuration, so pragma, `.editorconfig`, and supported `SuppressMessage` suppression work before findings are mapped. Documentation will show suppression examples and require or strongly encourage an explanatory justification. No EFDoctor-specific suppression file is introduced.

Alternative considered: collect suppressed diagnostics and filter them in the CLI. Rejected because it would duplicate Roslyn behavior and risk inconsistent IDE/CLI results.

### 5. Map diagnostics into an immutable finding contract

`EFDoctor.Core` defines immutable records/enums for severity, confidence, source range, finding, scan summary, and the version-1 report envelope. The CLI mapper converts Roslyn's zero-based source span to one-based user coordinates and requires every EFD001 contract field to be present.

Before rendering, normalize each source path consistently and sort findings by path using ordinal comparison, then start line, start column, end line, end column, and rule ID. Both renderers receive the same sorted list. Console rendering uses plain text as the baseline and optional ANSI emphasis only when color is enabled. JSON rendering uses `System.Text.Json`, emits numeric schema version `1`, and never emits ANSI, progress, or banner text to standard output.

Alternative considered: separate console and JSON projections directly from Roslyn diagnostics. Rejected because it would duplicate mapping and ordering logic and make output contracts drift.

### 6. Use a single explicit analysis command

Expose:

```text
efdoctor analyze <solution-or-project-path> [--format console|json] [--quiet] [--no-color]
```

Command parsing validates required arguments and supported file types before workspace loading. The CLI registers the installed .NET/MSBuild environment, opens a solution or project through Roslyn's MSBuild workspace, collects load failures as analysis errors, runs the analyzer against analyzable C# compilations, maps effective diagnostics, and renders once.

`--quiet` removes nonessential progress/decorative output but not the requested result or errors. `--no-color` disables ANSI sequences. JSON mode implicitly behaves as no-color and reserves stdout for JSON; failures in JSON mode use a schema-compatible error representation where possible, with human diagnostics sent to stderr only when no report can be formed.

Exit codes are centralized constants: `0` for a successful scan with no findings, `1` for a successful scan with findings, and `2` for invalid input or analysis failure. Expected findings are therefore not treated as execution errors.

Alternative considered: overload exit code `1` for both findings and failures. Rejected because automation must distinguish a valid result from an unusable scan.

### 7. Test the rule at analyzer and process boundaries

Use xUnit with Roslyn analyzer-testing support for focused diagnostics, markup-based locations, and configurable EF Core references. Maintain at least ten positive and ten negative cases, including:

- positives for `for`, `foreach`, `await foreach`, `while`, and `do`/`while`;
- synchronous and asynchronous overloads, cancellation-token usage, custom contexts, and overrides;
- nesting under `if`, `try`, and other ordinary blocks;
- multiple invocations and exact line/column assertions;
- negatives for unrelated same-name methods, saves outside loops, declarations/comments/strings/inactive code, unresolved symbols, and lambda/local-function boundaries;
- pragma, editor-configuration, and supported `SuppressMessage` suppression.

Reporting tests verify completeness, language, path/span conversion, ordering, no-color behavior, and JSON schema version `1`. At least one process-level CLI test invokes the built CLI against `tests/Fixtures/EFD001.Sample`, asserts exit code `1`, and verifies both console and JSON outputs. Clean/no-finding and invalid-path cases assert exit codes `0` and `2` respectively.

Alternative considered: test only through the CLI fixture. Rejected because analyzer edge cases need small isolated inputs, while the process test is still required to prove workspace loading and end-to-end reporting.

### 8. Enforce local-only behavior by construction

Production projects include no telemetry, HTTP client, remote logging, license validation, or update-check dependency. Analysis reads the supplied local solution/project and files resolved by its local build environment. The CLI does not run restore. Documentation distinguishes build-time dependency restoration from EFDoctor runtime behavior.

A repository-level dependency review and a process test with telemetry opt-out/environment isolation provide regression evidence. This does not claim that a user's own MSBuild targets are harmless; loading an untrusted project can execute its design-time build targets, so documentation must warn users to analyze only code they trust.

## Risks / Trade-offs

- **[MSBuild workspace may fail on an un-restored or SDK-incompatible target]** → Validate workspace diagnostics, return exit code `2`, and document the prerequisite to restore/build the target locally first.
- **[Loading a project can execute project-authored MSBuild targets]** → Document the trust boundary clearly; local-first means no EFDoctor data transmission, not sandboxing arbitrary project build logic.
- **[Pure lexical scope misses helper calls or immediately invoked deferred callables]** → Document this first-version limitation and keep findings high-confidence; consider interprocedural analysis only after precision data justifies it.
- **[Analyzer and CLI can drift on metadata fields]** → Keep EFD001 metadata constants in one analyzer definition and require the mapper to reject or test missing required properties.
- **[Suppression behavior can vary by Roslyn host and target form]** → Cover each supported mechanism in tests and phrase `SuppressMessage` support according to Roslyn host capabilities.
- **[Absolute paths reduce cross-machine byte-for-byte reproducibility]** → Guarantee deterministic ordering on a machine for this slice and keep path normalization isolated so a future schema can introduce root-relative paths deliberately.
- **[Exit code `1` may be interpreted as generic failure by naive wrappers]** → Document it prominently and verify it in end-to-end tests.

## Migration Plan

1. Add shared build/package configuration and the solution/project skeleton.
2. Add the analyzer and its focused fixtures before wiring the CLI.
3. Add finding mapping, ordering, and renderers, then workspace orchestration and command parsing.
4. Add the process fixture and end-to-end tests, followed by developer and suppression documentation.
5. Verify restore/build/test from a clean checkout and manually inspect console and JSON output.

There is no production-data migration. Rollback consists of reverting the newly introduced solution, projects, fixtures, and documentation because the repository has no prior executable release or persisted EFDoctor state.
