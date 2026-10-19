using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace EFDoctor.Analyzers.Tests;

// Runs an analyzer over a fixture in an AdhocWorkspace, offers its diagnostics to a code-fix
// provider, and applies the first action. Each fixture compiles against the same references as
// the analyzer tests.
internal static class CodeFixTestHarness
{
    // The fixed source of the document, or null when the provider offers no fix.
    public static async Task<string?> ApplyAsync(
        string source,
        DiagnosticAnalyzer analyzer,
        CodeFixProvider provider,
        bool includeRelational = true,
        int diagnosticIndex = 0)
    {
        var (document, diagnostics) = await AnalyzeAsync(source, analyzer, includeRelational);
        Assert.True(diagnosticIndex < diagnostics.Length, $"Expected a diagnostic at index {diagnosticIndex}, found {diagnostics.Length}.");

        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostics[diagnosticIndex], (action, _) => actions.Add(action), CancellationToken.None);
        await provider.RegisterCodeFixesAsync(context);
        if (actions.Count == 0)
        {
            return null;
        }

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    // Applies the provider's fix-all to every diagnostic in the document.
    public static async Task<string> FixAllAsync(string source, DiagnosticAnalyzer analyzer, CodeFixProvider provider, bool includeRelational = true)
    {
        var (document, diagnostics) = await AnalyzeAsync(source, analyzer, includeRelational);
        CodeAction? firstAction = null;
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostics[0], (action, _) => firstAction ??= action, CancellationToken.None));
        Assert.NotNull(firstAction);

        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            firstAction!.EquivalenceKey,
            provider.FixableDiagnosticIds,
            new FixedDiagnosticProvider(diagnostics),
            CancellationToken.None);
        var fixAll = await provider.GetFixAllProvider()!.GetFixAsync(fixAllContext);
        var operations = await fixAll!.GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();
    }

    public static void AssertCompiles(string source, bool includeRelational = true) =>
        Assert.DoesNotContain(
            AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: includeRelational),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    private static async Task<(Document Document, ImmutableArray<Diagnostic> Diagnostics)> AnalyzeAsync(string source, DiagnosticAnalyzer analyzer, bool includeRelational)
    {
        var template = AnalyzerTestHarness.CreateCompilation(source, true, includeRelational, false, false, false, null, "EFD000");
        var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "Fixture",
                "Fixture",
                LanguageNames.CSharp,
                compilationOptions: template.Options,
                parseOptions: template.SyntaxTrees[0].Options,
                metadataReferences: template.References))
            .AddDocument(documentId, "Fixture.cs", SourceText.From(source));
        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = (await compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync())
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToImmutableArray();
        return (document, diagnostics);
    }

    private sealed class FixedDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(ImmutableArray<Diagnostic>.Empty);
    }
}
