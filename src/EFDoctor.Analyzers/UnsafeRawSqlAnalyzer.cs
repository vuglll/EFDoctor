using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeRawSqlAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD012";
    public const string RuleTitle = "Dynamic SQL passed to an EF Core raw-SQL API";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD012";
    public const string Impact = "Dynamic raw SQL can permit SQL injection when untrusted values influence the text and can reduce query-plan reuse; this diagnostic proves the construction shape, not exploitability or measured runtime cost.";

    private const string RelationalQueryableExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string RelationalDatabaseFacadeExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions";
    private const string Message = "This EF Core raw-SQL call uses dynamically constructed SQL text";
    private const string Remediation = "Pass values separately with SQL parameter placeholders, or use the corresponding interpolated-safe EF Core API. SQL identifiers and structural fragments cannot be value parameters; select those from an explicit allow-list before composing SQL.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports interpolation or non-constant concatenation used as SQL text for supported EF Core raw-SQL APIs.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var queryableExtensions = context.Compilation.GetTypeByMetadataName(RelationalQueryableExtensionsMetadataName);
        var databaseExtensions = context.Compilation.GetTypeByMetadataName(RelationalDatabaseFacadeExtensionsMetadataName);
        if (queryableExtensions is null || databaseExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, queryableExtensions, databaseExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol queryableExtensions,
        INamedTypeSymbol databaseExtensions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (!IsSupportedRawMethod(method, queryableExtensions, databaseExtensions))
        {
            return;
        }

        var sqlArgument = invocation.Arguments.FirstOrDefault(static argument =>
            argument.Parameter?.Name == "sql"
            && argument.Parameter.Type.SpecialType == SpecialType.System_String);
        if (sqlArgument is null)
        {
            return;
        }

        var visitedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (!TryClassifyUnsafeSql(sqlArgument.Value, sqlArgument, visitedLocals, out var construction))
        {
            return;
        }

        var operationName = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Resolved EF Core raw-SQL operation '{operationName}' receives SQL text proven to originate from {construction}.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, sqlArgument.Value.Syntax.GetLocation(), properties));
    }

    private static bool IsSupportedRawMethod(
        IMethodSymbol method,
        INamedTypeSymbol queryableExtensions,
        INamedTypeSymbol databaseExtensions)
    {
        if (method.Name == "FromSqlRaw"
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, queryableExtensions))
        {
            return true;
        }

        return method.Name is "ExecuteSqlRaw" or "ExecuteSqlRawAsync"
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, databaseExtensions);
    }

    private static bool TryClassifyUnsafeSql(
        IOperation operation,
        IOperation useSite,
        ISet<ISymbol> visitedLocals,
        out string construction)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (operation is IInterpolatedStringOperation)
        {
            construction = "string interpolation";
            return true;
        }

        if (operation is IBinaryOperation binary
            && binary.OperatorKind == BinaryOperatorKind.Add
            && binary.OperatorMethod is null
            && binary.Type?.SpecialType == SpecialType.System_String
            && !binary.ConstantValue.HasValue)
        {
            construction = "non-constant string concatenation";
            return true;
        }

        if (operation is ILocalReferenceOperation localReference
            && visitedLocals.Add(localReference.Local)
            && TryGetUniqueStraightLineValue(localReference.Local, useSite, out var value)
            && TryClassifyUnsafeSql(value, useSite, visitedLocals, out var localConstruction))
        {
            construction = $"a local variable assigned from {localConstruction}";
            return true;
        }

        construction = string.Empty;
        return false;
    }

    private static bool TryGetUniqueStraightLineValue(
        ILocalSymbol local,
        IOperation useSite,
        out IOperation value)
    {
        var root = useSite;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var useBlock = FindContainingBlock(useSite);
        IOperation? candidate = null;
        var writes = 0;
        foreach (var operation in DescendantsAndSelf(root))
        {
            if (operation.Syntax.SpanStart >= useSite.Syntax.SpanStart)
            {
                continue;
            }

            if (IsUnsupportedWrite(operation, local))
            {
                value = useSite;
                return false;
            }

            IOperation? writtenValue = operation switch
            {
                IVariableDeclaratorOperation declarator
                    when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local)
                    && declarator.Initializer is not null => declarator.Initializer.Value,
                ISimpleAssignmentOperation assignment
                    when IsLocalReference(assignment.Target, local) => assignment.Value,
                _ => null,
            };

            if (writtenValue is null)
            {
                continue;
            }

            writes++;
            candidate = writtenValue;
            if (writes > 1 || !ReferenceEquals(FindContainingBlock(operation), useBlock))
            {
                value = useSite;
                return false;
            }
        }

        value = candidate ?? useSite;
        return writes == 1;
    }

    private static bool IsUnsupportedWrite(IOperation operation, ILocalSymbol local) =>
        operation switch
        {
            ICompoundAssignmentOperation assignment => IsLocalReference(assignment.Target, local),
            IIncrementOrDecrementOperation increment => IsLocalReference(increment.Target, local),
            IArgumentOperation argument when argument.Parameter?.RefKind != RefKind.None =>
                IsLocalReference(argument.Value, local),
            _ => false,
        };

    private static bool IsLocalReference(IOperation operation, ILocalSymbol local) =>
        EfQueryOperationAnalysis.Unwrap(operation) is ILocalReferenceOperation reference
        && SymbolEqualityComparer.Default.Equals(reference.Local, local);

    private static IBlockOperation? FindContainingBlock(IOperation operation)
    {
        for (var current = operation; current is not null; current = current.Parent)
        {
            if (current is IBlockOperation block)
            {
                return block;
            }
        }

        return null;
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }
}
