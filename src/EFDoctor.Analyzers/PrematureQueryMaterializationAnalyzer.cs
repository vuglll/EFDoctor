using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PrematureQueryMaterializationAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD004";
    public const string RuleTitle = "Query is materialized before SQL-capable composition";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD004";

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This EF Core query is materialized before {0}, so supported work may run over buffered client data";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports proven EF Core queries materialized immediately before simple SQL-capable LINQ-to-Objects composition.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        if (enumerable is null || queryable is null || dbSet is null || dbContext is null || efExtensions is null)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, enumerable, queryable, dbSet, dbContext, efExtensions),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions)
    {
        var materializer = (IInvocationOperation)context.Operation;
        if (!TryClassifyMaterializer(materializer, enumerable, efExtensions, out var asynchronous))
        {
            return;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(materializer);
        if (source is null
            || !EfQueryOperationAnalysis.TryAnalyzeSource(
                source,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                out var queryAnalysis))
        {
            return;
        }

        if (!TryGetEligibleConsumer(materializer, asynchronous, enumerable, out var consumer))
        {
            return;
        }

        var operatorName = consumer.TargetMethod.Name;
        var impact = operatorName switch
        {
            "Where" or "Skip" or "Take" => "Early materialization can transfer and buffer rows that the database could otherwise filter or limit; actual impact depends on result size and query semantics.",
            "Select" => "Early materialization can retrieve unnecessary columns or tracked entities before projection; actual impact depends on result shape and tracking semantics.",
            _ => "Early materialization makes ordering run over buffered client data instead of composing it into the database query; actual impact depends on result size and query semantics.",
        };
        const string remediation = "Consider moving the supported operation before materialization so EF Core can compose it into SQL, while preserving semantics and verifying generated SQL. Keeping client-side composition can be legitimate for small bounded results, intentional client-only logic, repeated reuse, provider translation limitations, or tracking and semantic requirements.";
        var methodDisplay = materializer.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Invocation resolves to '{methodDisplay}', its source is traced to DbSet origin '{queryAnalysis.Origin.Syntax}', and immediate '{operatorName}' composition will run over the buffered result.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(Rule, materializer.Syntax.GetLocation(), properties, operatorName));
    }

    internal static bool TryClassifyMaterializer(
        IInvocationOperation invocation,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol efExtensions,
        out bool asynchronous)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        asynchronous = false;
        if (SymbolEqualityComparer.Default.Equals(containingType, enumerable))
        {
            return method.Name is "ToList" or "ToArray";
        }

        if (SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
            && method.Name is "ToListAsync" or "ToArrayAsync")
        {
            asynchronous = true;
            return true;
        }

        return false;
    }

    internal static bool TryGetEligibleConsumer(
        IInvocationOperation materializer,
        bool asynchronous,
        INamedTypeSymbol enumerable,
        out IInvocationOperation consumer)
    {
        if (!TryGetMaterializedValue(materializer, asynchronous, out var current))
        {
            consumer = null!;
            return false;
        }

        IInvocationOperation? invocation = null;
        if (current.Parent is IInvocationOperation directInvocation && directInvocation.Instance == current)
        {
            invocation = directInvocation;
        }
        else if (current.Parent is IArgumentOperation argument
            && argument.Parameter?.Ordinal == 0
            && argument.Parent is IInvocationOperation argumentInvocation)
        {
            invocation = argumentInvocation;
        }

        if (invocation is null)
        {
            consumer = null!;
            return false;
        }

        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        if (!SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, enumerable)
            || method.Name is not ("Where" or "Select" or "OrderBy" or "OrderByDescending" or "Skip" or "Take"))
        {
            consumer = null!;
            return false;
        }

        if (!HasSupportedArguments(invocation))
        {
            consumer = null!;
            return false;
        }

        consumer = invocation;
        return true;
    }

    // Returns the outermost wrapper of the materialized value: implicit conversions and
    // parentheses, plus the single required await for an asynchronous materializer. Rules that
    // inspect the immediate consumer of a materialized EF query share this walk.
    internal static bool TryGetMaterializedValue(
        IInvocationOperation materializer,
        bool asynchronous,
        out IOperation value)
    {
        IOperation current = materializer;
        var observedAwait = false;
        while (current.Parent is not null)
        {
            switch (current.Parent)
            {
                case IConversionOperation conversion when conversion.Operand == current && conversion.IsImplicit:
                    current = conversion;
                    continue;
                case IParenthesizedOperation parenthesized when parenthesized.Operand == current:
                    current = parenthesized;
                    continue;
                case IAwaitOperation awaitOperation when asynchronous && !observedAwait && awaitOperation.Operation == current:
                    observedAwait = true;
                    current = awaitOperation;
                    continue;
            }

            break;
        }

        value = current;
        return !asynchronous || observedAwait;
    }

    private static bool HasSupportedArguments(IInvocationOperation consumer)
    {
        var arguments = consumer.Arguments
            .Where(static argument => argument.Parameter?.Ordinal != 0)
            .OrderBy(static argument => argument.Parameter?.Ordinal)
            .Select(static argument => argument.Value)
            .ToArray();

        return consumer.TargetMethod.Name switch
        {
            "Where" => arguments.Length == 1 && EfQueryOperationAnalysis.TryGetLambda(arguments[0], out var whereLambda) && whereLambda.Symbol.Parameters.Length == 1 && EfQueryOperationAnalysis.IsPredicateExpression(EfQueryOperationAnalysis.GetLambdaExpression(whereLambda), whereLambda.Symbol.Parameters[0]),
            "Select" => arguments.Length == 1 && EfQueryOperationAnalysis.TryGetLambda(arguments[0], out var selectLambda) && selectLambda.Symbol.Parameters.Length == 1 && IsProjection(EfQueryOperationAnalysis.GetLambdaExpression(selectLambda), selectLambda.Symbol.Parameters[0]),
            "OrderBy" or "OrderByDescending" => arguments.Length == 1 && EfQueryOperationAnalysis.TryGetLambda(arguments[0], out var orderLambda) && orderLambda.Symbol.Parameters.Length == 1 && EfQueryOperationAnalysis.IsEntityPropertyPath(EfQueryOperationAnalysis.GetLambdaExpression(orderLambda), orderLambda.Symbol.Parameters[0]),
            "Skip" or "Take" => arguments.Length == 1 && IsStableIntegralValue(arguments[0]),
            _ => false,
        };
    }

    private static bool IsProjection(IOperation? operation, IParameterSymbol entityParameter)
    {
        if (operation is null)
        {
            return false;
        }

        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (EfQueryOperationAnalysis.IsEntityPropertyPath(operation, entityParameter))
        {
            return true;
        }

        return operation is IAnonymousObjectCreationOperation anonymous
            && anonymous.Initializers.All(initializer => initializer is ISimpleAssignmentOperation assignment
                && EfQueryOperationAnalysis.IsScalar(assignment.Value, entityParameter));
    }

    private static bool IsStableIntegralValue(IOperation operation)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (!IsIntegral(operation.Type))
        {
            return false;
        }

        return operation.ConstantValue.HasValue
            || operation is ILocalReferenceOperation
            || operation is IParameterReferenceOperation;
    }

    private static bool IsIntegral(ITypeSymbol? type)
    {
        return type?.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64;
    }

}
