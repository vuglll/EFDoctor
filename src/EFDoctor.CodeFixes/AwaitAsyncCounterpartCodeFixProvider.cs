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

// EFD010: a synchronous EF Core call in async code awaits its async counterpart instead.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AwaitAsyncCounterpartCodeFixProvider))]
[Shared]
public sealed class AwaitAsyncCounterpartCodeFixProvider : CodeFixProvider
{
    public const string DiagnosticId = "EFD010";

    private const string EfNamespace = "Microsoft.EntityFrameworkCore";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";

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
            if (!TryPlan(root, diagnostic, model, context.CancellationToken, out var plan))
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Await {plan.Counterpart}",
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(Apply(root, plan))),
                    equivalenceKey: nameof(AwaitAsyncCounterpartCodeFixProvider)),
                diagnostic);
        }
    }

    private static bool TryPlan(SyntaxNode root, Diagnostic diagnostic, SemanticModel model, CancellationToken cancellationToken, out Plan plan)
    {
        plan = null!;
        if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not { } node
            || node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault() is not { Expression: MemberAccessExpressionSyntax memberAccess } invocation
            || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        // Static form, such as `Queryable.Count(query)`, would need its containing type rewritten.
        if (method.IsStatic && method.ReducedFrom is null)
        {
            return false;
        }

        var counterpart = method.Name + "Async";
        var isExtension = method.ReducedFrom is not null;
        var efExtensions = model.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        var exists = isExtension
            ? efExtensions?.GetMembers(counterpart).Any() == true
            : method.ContainingType.GetMembers(counterpart).Any()
                || method.ContainingType.AllInterfaces.Any(type => type.GetMembers(counterpart).Any())
                || BaseTypes(method.ContainingType).Any(type => type.GetMembers(counterpart).Any());
        if (!exists || !IsAwaitAllowed(invocation, out var function))
        {
            return false;
        }

        var token = counterpart == "FindAsync" ? null : FindTokenInScope(function, model, cancellationToken);
        plan = new Plan(invocation, memberAccess, counterpart, token, needsUsing: isExtension);
        return true;
    }

    private static SyntaxNode Apply(SyntaxNode root, Plan plan)
    {
        var name = plan.MemberAccess.Name;
        var renamed = plan.MemberAccess.WithName(name.WithIdentifier(SyntaxFactory.Identifier(name.Identifier.LeadingTrivia, plan.Counterpart, name.Identifier.TrailingTrivia)));
        var arguments = plan.Invocation.ArgumentList;
        if (plan.Token is not null)
        {
            arguments = arguments.AddArguments(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(plan.Token)));
        }

        var call = plan.Invocation.WithExpression(renamed).WithArgumentList(arguments).WithoutTrivia();
        ExpressionSyntax awaited = SyntaxFactory.AwaitExpression(call);
        if (plan.Invocation.Parent is MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or ConditionalAccessExpressionSyntax or InvocationExpressionSyntax)
        {
            awaited = SyntaxFactory.ParenthesizedExpression(awaited);
        }

        var updated = root.ReplaceNode(plan.Invocation, awaited.WithTriviaFrom(plan.Invocation));
        return plan.NeedsUsing ? AddEfUsing(updated) : updated;
    }

    // `await` is allowed when the nearest enclosing function is async and no lock statement intervenes.
    private static bool IsAwaitAllowed(SyntaxNode node, out SyntaxNode function)
    {
        function = null!;
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case LockStatementSyntax:
                    return false;
                case AnonymousFunctionExpressionSyntax lambda:
                    function = lambda;
                    return lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax local:
                    function = local;
                    return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    function = method;
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case BaseMethodDeclarationSyntax or AccessorDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    // A CancellationToken parameter of the function or of one that encloses it.
    private static string? FindTokenInScope(SyntaxNode function, SemanticModel model, CancellationToken cancellationToken)
    {
        var tokenType = model.Compilation.GetTypeByMetadataName(CancellationTokenMetadataName);
        foreach (var scope in function.AncestorsAndSelf())
        {
            var parameters = scope switch
            {
                ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters.ToList(),
                SimpleLambdaExpressionSyntax lambda => new() { lambda.Parameter },
                LocalFunctionStatementSyntax local => local.ParameterList.Parameters.ToList(),
                BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.ToList(),
                _ => null,
            };
            foreach (var parameter in parameters ?? new())
            {
                if (model.GetDeclaredSymbol(parameter, cancellationToken) is IParameterSymbol symbol
                    && SymbolEqualityComparer.Default.Equals(symbol.Type, tokenType))
                {
                    return symbol.Name;
                }
            }

            if (scope is BaseMethodDeclarationSyntax)
            {
                break;
            }
        }

        return null;
    }

    private static SyntaxNode AddEfUsing(SyntaxNode root)
    {
        if (root is not CompilationUnitSyntax unit
            || unit.DescendantNodes().OfType<UsingDirectiveSyntax>().Any(static directive => directive.Name?.ToString() == EfNamespace))
        {
            return root;
        }

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(EfNamespace))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(unit.ToFullString().Contains("\r\n") ? "\r\n" : "\n"));
        return unit.AddUsings(directive);
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private sealed class Plan
    {
        public Plan(InvocationExpressionSyntax invocation, MemberAccessExpressionSyntax memberAccess, string counterpart, string? token, bool needsUsing)
        {
            Invocation = invocation;
            MemberAccess = memberAccess;
            Counterpart = counterpart;
            Token = token;
            NeedsUsing = needsUsing;
        }

        public InvocationExpressionSyntax Invocation { get; }

        public MemberAccessExpressionSyntax MemberAccess { get; }

        public string Counterpart { get; }

        public string? Token { get; }

        public bool NeedsUsing { get; }
    }
}
