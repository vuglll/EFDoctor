using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFDoctor.CodeFixes;

// EFD029: a second OrderBy discards the earlier ordering, where ThenBy was meant.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OrderByToThenByCodeFixProvider))]
[Shared]
public sealed class OrderByToThenByCodeFixProvider : CodeFixProvider
{
    public const string DiagnosticId = "EFD029";

    private const string OrderedQueryableMetadataName = "System.Linq.IOrderedQueryable`1";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!TryGetOrderingName(root, diagnostic, model, context.CancellationToken, out var name, out var replacement))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use {replacement}",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(name, Rename(name, replacement)))),
                    equivalenceKey: nameof(OrderByToThenByCodeFixProvider)),
                diagnostic);
        }
    }

    // The OrderBy name to rename, when the call's source is already an IOrderedQueryable<T>, so the
    // ThenBy that replaces it compiles.
    private static bool TryGetOrderingName(
        SyntaxNode root,
        Diagnostic diagnostic,
        SemanticModel model,
        CancellationToken cancellationToken,
        out SimpleNameSyntax name,
        out string replacement)
    {
        name = null!;
        replacement = null!;
        var invocation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(static candidate => candidate.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "OrderBy" or "OrderByDescending" });
        if (invocation?.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return false;
        }

        name = memberAccess.Name;
        replacement = name.Identifier.ValueText == "OrderBy" ? "ThenBy" : "ThenByDescending";

        // The receiver is the source in reduced syntax; the first argument in static syntax.
        var method = model.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
        var source = method?.ReducedFrom is not null || method is { IsStatic: false }
            ? memberAccess.Expression
            : invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        var orderedQueryable = model.Compilation.GetTypeByMetadataName(OrderedQueryableMetadataName);
        var sourceType = source is null ? null : model.GetTypeInfo(source, cancellationToken).Type;
        return orderedQueryable is not null && sourceType is not null && IsOrdered(sourceType, orderedQueryable);
    }

    private static bool IsOrdered(ITypeSymbol type, INamedTypeSymbol orderedQueryable) =>
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, orderedQueryable)
        || type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, orderedQueryable));

    private static SimpleNameSyntax Rename(SimpleNameSyntax name, string replacement) =>
        name.WithIdentifier(SyntaxFactory.Identifier(name.Identifier.LeadingTrivia, replacement, name.Identifier.TrailingTrivia));
}
