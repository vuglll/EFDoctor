# Design

## Context

`EFDoctor.Analyzers` compiles against `Microsoft.CodeAnalysis.CSharp` 4.8 with no Workspaces reference, so the .NET 8 SDK compiler can load it. Code-fix providers need `Microsoft.CodeAnalysis.Workspaces`, which only IDE and test hosts provide.

## Decisions

### A separate assembly, packed with the analyzers

**Decision:** Add `src/EFDoctor.CodeFixes`, which references `Microsoft.CodeAnalysis.CSharp.Workspaces` 4.8 privately. `EFDoctor.Analyzers.csproj` references it with `ReferenceOutputAssembly="false"`, so it builds first, and packs its DLL into `analyzers/dotnet/cs`.

**Why:**
- The command-line compiler loads only `DiagnosticAnalyzer` types and never needs Workspaces.
- IDEs load `CodeFixProvider`s from the same folder.
- This is the layout of the Roslyn analyzer template.

**Alternative considered:** putting fixes in the analyzer assembly. Rejected, because it would make the compiler-loaded assembly depend on Workspaces.

### Fixes refer to diagnostic IDs as strings

**Decision:** The fixes don't reference the analyzer assembly. Each declares its IDs as string constants, and the tests check that they match the analyzers' `DiagnosticId`.

**Why:** It keeps the build graph acyclic. The analyzer project builds the fixes first, for packing.

### Tests apply fixes in an AdhocWorkspace

**Decision:**
1. The `CodeFixTestHarness` builds a project with the same references as the analyzer tests.
2. It runs the analyzer, and offers each diagnostic to the provider.
3. It applies the first action's `ApplyChangesOperation`.
4. It returns the new source, which the tests compare exactly and recompile without errors.

### Offer a fix only when its result compiles

**Decision:** Each fix checks, with the semantic model, the condition its rewrite needs. For EFD029, that's a receiver typed `IOrderedQueryable<T>`. The fix isn't registered when the condition fails.

## Risks / Trade-offs

- **[Risk] Some hosts warn when an analyzer-folder assembly references Workspaces.** → The packaged-analyzer test builds a real project with the package, and requires no new warnings.
