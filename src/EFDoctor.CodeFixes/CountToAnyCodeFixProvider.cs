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

// EFD002: a count used only to test existence becomes Any, or its negation.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CountToAnyCodeFixProvider))]
[Shared]
public sealed class CountToAnyCodeFixProvider : CodeFixProvider
{
    public const string DiagnosticId = "EFD002";

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
            if (!TryGetRewrite(root, diagnostic, model, context.CancellationToken, out var comparison, out var replacement, out var anyName))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Use {anyName}",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(comparison, replacement))),
                    equivalenceKey: nameof(CountToAnyCodeFixProvider)),
                diagnostic);
        }
    }

    private static bool TryGetRewrite(
        SyntaxNode root,
        Diagnostic diagnostic,
        SemanticModel model,
        CancellationToken cancellationToken,
        out BinaryExpressionSyntax comparison,
        out ExpressionSyntax replacement,
        out string anyName)
    {
        comparison = null!;
        replacement = null!;
        anyName = null!;
        var invocation = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(static candidate => MethodName(candidate) is { Identifier.ValueText: "Count" or "CountAsync" });
        if (invocation is null || MethodName(invocation) is not { } name)
        {
            return false;
        }

        // The count's value: the invocation, or `await invocation[.ConfigureAwait(…)]`, in parentheses.
        ExpressionSyntax value = invocation;
        if (value.Parent is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } configure
            && configure.Parent is InvocationExpressionSyntax configureCall)
        {
            value = configureCall;
        }

        if (value.Parent is AwaitExpressionSyntax awaited)
        {
            value = awaited;
        }

        while (value.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            value = parenthesized;
        }

        if (value.Parent is not BinaryExpressionSyntax binary)
        {
            return false;
        }

        var countIsLeft = binary.Left == value;
        var constant = model.GetConstantValue(countIsLeft ? binary.Right : binary.Left, cancellationToken);
        if (!constant.HasValue || constant.Value is null || !long.TryParse(constant.Value.ToString(), out var boundary))
        {
            return false;
        }

        var kind = countIsLeft ? binary.Kind() : Reverse(binary.Kind());
        bool? testsForEmpty = (kind, boundary) switch
        {
            (SyntaxKind.GreaterThanExpression, 0) or (SyntaxKind.NotEqualsExpression, 0) or (SyntaxKind.GreaterThanOrEqualExpression, 1) => false,
            (SyntaxKind.EqualsExpression, 0) or (SyntaxKind.LessThanOrEqualExpression, 0) or (SyntaxKind.LessThanExpression, 1) => true,
            _ => null,
        };
        if (testsForEmpty is null)
        {
            return false;
        }

        anyName = name.Identifier.ValueText == "CountAsync" ? "AnyAsync" : "Any";
        var renamed = name.WithIdentifier(SyntaxFactory.Identifier(name.Identifier.LeadingTrivia, anyName, name.Identifier.TrailingTrivia));
        var any = value.ReplaceNode(name, renamed).WithoutTrivia();
        replacement = (testsForEmpty.Value
                ? SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, any)
                : any)
            .WithTriviaFrom(binary);
        comparison = binary;
        return true;
    }

    private static SimpleNameSyntax? MethodName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
        SimpleNameSyntax simple => simple,
        _ => null,
    };

    private static SyntaxKind Reverse(SyntaxKind kind) => kind switch
    {
        SyntaxKind.GreaterThanExpression => SyntaxKind.LessThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression => SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.LessThanExpression => SyntaxKind.GreaterThanExpression,
        SyntaxKind.LessThanOrEqualExpression => SyntaxKind.GreaterThanOrEqualExpression,
        _ => kind,
    };
}
