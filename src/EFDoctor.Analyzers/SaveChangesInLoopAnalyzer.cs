using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SaveChangesInLoopAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD001";
    public const string RuleTitle = "SaveChanges executed inside a loop";
    public const string Confidence = "high";
    public const string Impact = "Saving within a loop can create repeated database round trips and prevent effective batching.";
    public const string Remediation = "Consider collecting changes and saving once after the loop. Retain per-item saves when intentional per-item transactions, immediate generated-key requirements, partial-failure boundaries, or strict consistency requirements justify them.";
    public const string DocumentationKey = "EFD001";

    public const string ConfidenceProperty = DiagnosticPropertyNames.Confidence;
    public const string EvidenceProperty = DiagnosticPropertyNames.Evidence;
    public const string ImpactProperty = DiagnosticPropertyNames.LikelyImpact;
    public const string RemediationProperty = DiagnosticPropertyNames.SuggestedRemediation;
    public const string DocumentationProperty = DiagnosticPropertyNames.DocumentationReference;

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string Message = "This EF Core save executes inside a loop and may cause repeated database round trips";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports semantically resolved EF Core SaveChanges calls that execute once per item in supported loop bodies; loops that save once per chunk, page, or threshold-flushed batch are not reported.",
        helpLinkUri: EfHelpLinks.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        if (!EfAnalysisScope.Includes(context.Options, context.Compilation))
        {
            return;
        }

        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        if (dbContext is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, dbContext),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol dbContext)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsEfCoreSaveMethod(invocation.TargetMethod, dbContext))
        {
            return;
        }

        var loop = FindEnclosingLoop(invocation.Syntax);
        if (loop is null
            || invocation.SemanticModel is not { } model
            || SavesOncePerBatch(loop, invocation.Syntax, model, context.CancellationToken))
        {
            return;
        }

        var method = invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var loopKind = GetLoopKind(loop);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, $"Invocation resolves to '{method}' and is lexically inside a {loopKind} loop.")
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties));
    }

    private static bool IsEfCoreSaveMethod(IMethodSymbol method, INamedTypeSymbol dbContext)
    {
        if (method.IsStatic || (method.Name != "SaveChanges" && method.Name != "SaveChangesAsync"))
        {
            return false;
        }

        for (IMethodSymbol? candidate = method; candidate is not null; candidate = candidate.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.ContainingType.OriginalDefinition, dbContext))
            {
                return true;
            }
        }

        return false;
    }

    private static SyntaxNode? FindEnclosingLoop(SyntaxNode invocation)
    {
        for (var current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (IsSupportedLoop(current))
            {
                return current;
            }

            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax)
            {
                return null;
            }
        }

        return null;
    }

    // The innermost loop saves once per batch when one iteration handles a chunk or page,
    // or when the save only runs once a threshold of processed items is reached.
    private static bool SavesOncePerBatch(SyntaxNode loop, SyntaxNode save, SemanticModel model, CancellationToken cancellationToken)
    {
        if (loop is ForEachStatementSyntax forEach)
        {
            if (IsCollectionType(model.GetForEachStatementInfo(forEach).ElementType))
            {
                return true;
            }
        }
        else if (DeclaresCollectionInCondition(loop, model, cancellationToken)
            || IteratesPageLoadedInBody(loop, save, model, cancellationToken))
        {
            return true;
        }

        return IsInsideThresholdFlush(loop, save);
    }

    private static bool DeclaresCollectionInCondition(SyntaxNode loop, SemanticModel model, CancellationToken cancellationToken)
    {
        IEnumerable<SyntaxNode> parts = loop switch
        {
            WhileStatementSyntax @while => new[] { @while.Condition },
            DoStatementSyntax @do => new[] { @do.Condition },
            ForStatementSyntax @for => @for.Initializers.Cast<SyntaxNode>()
                .Concat(@for.Condition is null ? Array.Empty<SyntaxNode>() : new SyntaxNode[] { @for.Condition }),
            _ => Array.Empty<SyntaxNode>(),
        };

        foreach (var node in parts.SelectMany(static part => part.DescendantNodesAndSelf()))
        {
            var type = node switch
            {
                SingleVariableDesignationSyntax designation =>
                    (model.GetDeclaredSymbol(designation, cancellationToken) as ILocalSymbol)?.Type,
                AssignmentExpressionSyntax assignment
                    when model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is ILocalSymbol local => local.Type,
                _ => null,
            };
            if (IsCollectionType(type))
            {
                return true;
            }
        }

        return false;
    }

    // `while (true) { var page = …; foreach (var item in page) { … } Save(); }`
    private static bool IteratesPageLoadedInBody(SyntaxNode loop, SyntaxNode save, SemanticModel model, CancellationToken cancellationToken)
    {
        var body = loop switch
        {
            WhileStatementSyntax @while => @while.Statement,
            DoStatementSyntax @do => @do.Statement,
            ForStatementSyntax @for => @for.Statement,
            _ => null,
        };
        if (body is null)
        {
            return false;
        }

        foreach (var inner in body.DescendantNodes().OfType<CommonForEachStatementSyntax>())
        {
            if (inner.Span.End <= save.SpanStart
                && GetRootIdentifier(inner.Expression) is { } identifier
                && model.GetSymbolInfo(identifier, cancellationToken).Symbol is ILocalSymbol local
                && IsCollectionType(local.Type)
                && local.DeclaringSyntaxReferences.Any(reference => body.Span.Contains(reference.Span)))
            {
                return true;
            }
        }

        return false;
    }

    // `page`, `page.Where(...)`, or `page.AsAsyncEnumerable()` all iterate the page.
    private static IdentifierNameSyntax? GetRootIdentifier(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case IdentifierNameSyntax identifier:
                    return identifier;
                case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access }:
                    expression = access.Expression;
                    continue;
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                default:
                    return null;
            }
        }
    }

    private static bool IsInsideThresholdFlush(SyntaxNode loop, SyntaxNode save)
    {
        for (var current = save; current != loop && current.Parent is { } parent; current = parent)
        {
            if (parent is IfStatementSyntax @if
                && @if.Statement == current
                && @if.Condition.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>().Any(comparison => IsThreshold(comparison, loop)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsThreshold(BinaryExpressionSyntax comparison, SyntaxNode loop)
    {
        if (!comparison.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
            && !comparison.IsKind(SyntaxKind.GreaterThanExpression)
            && !comparison.IsKind(SyntaxKind.EqualsExpression))
        {
            return false;
        }

        return IsCounterSide(comparison.Left, loop) || IsCounterSide(comparison.Right, loop);
    }

    private static bool IsCounterSide(ExpressionSyntax side, SyntaxNode loop)
    {
        while (side is ParenthesizedExpressionSyntax parenthesized)
        {
            side = parenthesized.Expression;
        }

        return side switch
        {
            BinaryExpressionSyntax modulo when modulo.IsKind(SyntaxKind.ModuloExpression) => IsCounterSide(modulo.Left, loop),
            IdentifierNameSyntax identifier => IsIncrementedWithin(identifier.Identifier.ValueText, loop),
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText is "Count" or "Length",
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Count" } } => true,
            _ => false,
        };
    }

    private static bool IsIncrementedWithin(string name, SyntaxNode loop) =>
        loop.DescendantNodes().Any(node => node switch
        {
            PostfixUnaryExpressionSyntax postfix => IsName(postfix.Operand, name),
            PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression) => IsName(prefix.Operand, name),
            AssignmentExpressionSyntax assignment
                when assignment.IsKind(SyntaxKind.AddAssignmentExpression) || assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) => IsName(assignment.Left, name),
            _ => false,
        });

    private static bool IsName(ExpressionSyntax expression, string name) =>
        expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;

    private static bool IsCollectionType(ITypeSymbol? type) =>
        type is not null
        && type.SpecialType != SpecialType.System_String
        && (type is IArrayTypeSymbol
            || type.AllInterfaces.Any(static @interface => @interface.SpecialType == SpecialType.System_Collections_IEnumerable));

    private static bool IsSupportedLoop(SyntaxNode node)
    {
        return node is ForStatementSyntax
            or ForEachStatementSyntax
            or ForEachVariableStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax;
    }

    private static string GetLoopKind(SyntaxNode loop)
    {
        return loop switch
        {
            ForStatementSyntax => "for",
            ForEachStatementSyntax forEach when !forEach.AwaitKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None) => "await foreach",
            ForEachStatementSyntax or ForEachVariableStatementSyntax => "foreach",
            WhileStatementSyntax => "while",
            DoStatementSyntax => "do/while",
            _ => "supported",
        };
    }
}
