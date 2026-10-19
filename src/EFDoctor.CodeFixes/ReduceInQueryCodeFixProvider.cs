using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFDoctor.CodeFixes;

// EFD019: a query materialized only to be reduced on the client reduces in the query instead.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReduceInQueryCodeFixProvider))]
[Shared]
public sealed class ReduceInQueryCodeFixProvider : CodeFixProvider
{
    public const string DiagnosticId = "EFD019";

    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!TryGetRewrite(root, diagnostic, out var reduction, out var replacement))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Reduce in the query",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(reduction, replacement))),
                    equivalenceKey: nameof(ReduceInQueryCodeFixProvider)),
                diagnostic);
        }
    }

    private static bool TryGetRewrite(SyntaxNode root, Diagnostic diagnostic, out ExpressionSyntax reduction, out ExpressionSyntax replacement)
    {
        reduction = null!;
        replacement = null!;
        var materializer = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(static candidate => candidate.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToList" or "ToArray" or "ToListAsync" or "ToArrayAsync" });
        if (materializer?.Expression is not MemberAccessExpressionSyntax { Expression: var source, Name.Identifier.ValueText: var materializerName })
        {
            return false;
        }

        var isAsync = materializerName.EndsWith("Async", System.StringComparison.Ordinal);

        // The materialized value: `source.ToList()`, or `(await source.ToListAsync())`.
        ExpressionSyntax value = materializer;
        if (isAsync)
        {
            if (value.Parent is not AwaitExpressionSyntax awaited || awaited.Parent is not ParenthesizedExpressionSyntax parenthesized)
            {
                return false;
            }

            value = parenthesized;
        }

        if (value.Parent is not MemberAccessExpressionSyntax { Expression: var receiver } access || receiver != value)
        {
            return false;
        }

        var token = isAsync ? materializer.ArgumentList.Arguments.FirstOrDefault() : null;
        var reducerName = access.Name.Identifier.ValueText;
        ArgumentListSyntax arguments;
        if (access.Parent is InvocationExpressionSyntax reducerCall && reducerCall.Expression == access)
        {
            reduction = reducerCall;
            arguments = reducerCall.ArgumentList;
        }
        else if (reducerName is "Count" or "Length")
        {
            reduction = access;
            reducerName = "Count";
            arguments = SyntaxFactory.ArgumentList();
        }
        else
        {
            return false;
        }

        if (token is not null)
        {
            arguments = arguments.AddArguments(token.WithoutTrivia());
        }

        var call = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                source.WithoutTrailingTrivia(),
                SyntaxFactory.IdentifierName(isAsync ? reducerName + "Async" : reducerName)),
            arguments);
        ExpressionSyntax result = isAsync ? SyntaxFactory.AwaitExpression(call) : call;
        replacement = result.WithTriviaFrom(reduction);
        return true;
    }
}
