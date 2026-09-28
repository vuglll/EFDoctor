using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BulkUpdateDeleteAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD013";
    public const string RuleTitle = "Load-loop-save pattern may support a bulk operation";
    public const string Confidence = "medium";
    public const string DocumentationKey = "EFD013";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string Message = "This load-{0}-save pattern may be replaceable with {1}";
    private const string Impact = "Materializing matching entities before a uniform bulk operation can transfer rows, allocate tracked entities, perform change detection, and generate per-row modification commands; actual cost depends on row count, mappings, provider behavior, and batching.";
    private const string Remediation = "Review whether the original query can use {0} directly. Bulk operations bypass tracked-entity state and SaveChanges behavior; verify provider translation, concurrency-token handling, interceptors and domain callbacks, cascade behavior, transaction boundaries, and any already-tracked entities before changing the code.";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports conservative EF Core load-loop-save patterns that may support set-based bulk update or delete operations.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        var enumerable = context.Compilation.GetTypeByMetadataName(EnumerableMetadataName);
        var queryable = context.Compilation.GetTypeByMetadataName(QueryableMetadataName);
        if (dbContext is null || dbSet is null || efExtensions is null || enumerable is null || queryable is null)
        {
            return;
        }

        var bulkMethods = efExtensions.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method => method.Name is "ExecuteUpdate" or "ExecuteUpdateAsync" or "ExecuteDelete" or "ExecuteDeleteAsync")
            .Select(EfQueryOperationAnalysis.NormalizeMethod)
            .ToImmutableArray();
        if (bulkMethods.IsEmpty)
        {
            return;
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeLoop(
                operationContext,
                dbContext,
                dbSet,
                efExtensions,
                enumerable,
                queryable,
                bulkMethods),
            OperationKind.Loop);
    }

    private static void AnalyzeLoop(
        OperationAnalysisContext context,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        ImmutableArray<IMethodSymbol> bulkMethods)
    {
        if (context.Operation is not IForEachLoopOperation loop
            || loop.Locals.Length != 1
            || loop.Parent is not IBlockOperation block
            || !TryGetMaterializer(loop, block, enumerable, efExtensions, out var materializer)
            || !TryAnalyzeMaterializer(
                materializer,
                enumerable,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                out var query))
        {
            return;
        }

        if (!TryGetFollowingSave(loop, block, dbContext, query.ContextIdentity, out var saveMethod, out var asynchronousSave))
        {
            return;
        }

        var iterationLocal = loop.Locals[0];
        string action;
        string bulkMethod;
        string actionEvidence;
        if (TryClassifyUpdate(loop.Body, iterationLocal, out var propertyNames))
        {
            action = "update";
            bulkMethod = asynchronousSave ? "ExecuteUpdateAsync" : "ExecuteUpdate";
            if (!HasBulkMethod(bulkMethods, bulkMethod))
            {
                return;
            }

            actionEvidence = $"uniform assignments to {string.Join(", ", propertyNames)}";
        }
        else if (TryClassifyDelete(
            loop.Body,
            iterationLocal,
            dbContext,
            dbSet,
            query.ContextIdentity,
            query.SetIdentity,
            out var deleteMethod))
        {
            action = "delete";
            bulkMethod = asynchronousSave ? "ExecuteDeleteAsync" : "ExecuteDelete";
            if (!HasBulkMethod(bulkMethods, bulkMethod))
            {
                return;
            }

            actionEvidence = $"resolved per-entity delete '{deleteMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}'";
        }
        else
        {
            return;
        }

        var materializerName = materializer.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var saveName = saveMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var evidence = $"Resolved EF Core materializer '{materializerName}' over query origin '{query.Origin.Syntax}', followed by {actionEvidence} and matching save '{saveName}'.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, string.Format(Remediation, bulkMethod))
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            materializer.Syntax.GetLocation(),
            properties,
            action,
            bulkMethod));
    }

    private static bool TryGetMaterializer(
        IForEachLoopOperation loop,
        IBlockOperation block,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol efExtensions,
        out IInvocationOperation materializer)
    {
        var collection = UnwrapAwait(loop.Collection);
        if (collection is IInvocationOperation direct
            && IsMaterializer(direct, enumerable, efExtensions))
        {
            materializer = direct;
            return true;
        }

        if (collection is not ILocalReferenceOperation localReference)
        {
            materializer = null!;
            return false;
        }

        IVariableDeclaratorOperation? declaration = null;
        var loopIndex = IndexOf(block.Operations, loop);
        if (loopIndex < 0)
        {
            materializer = null!;
            return false;
        }

        for (var index = 0; index < loopIndex; index++)
        {
            foreach (var operation in DescendantsAndSelf(block.Operations[index]))
            {
                if (operation is IVariableDeclaratorOperation declarator
                    && SymbolEqualityComparer.Default.Equals(declarator.Symbol, localReference.Local))
                {
                    if (declaration is not null || declarator.Initializer is null)
                    {
                        materializer = null!;
                        return false;
                    }

                    declaration = declarator;
                    continue;
                }

                if (operation is ILocalReferenceOperation reference
                    && SymbolEqualityComparer.Default.Equals(reference.Local, localReference.Local))
                {
                    materializer = null!;
                    return false;
                }
            }
        }

        var value = declaration?.Initializer?.Value;
        value = value is null ? null : UnwrapAwait(value);
        if (value is IInvocationOperation invocation
            && IsMaterializer(invocation, enumerable, efExtensions))
        {
            materializer = invocation;
            return true;
        }

        materializer = null!;
        return false;
    }

    private static bool TryAnalyzeMaterializer(
        IInvocationOperation materializer,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol queryable,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        out QueryIdentity query)
    {
        if (!IsMaterializer(materializer, enumerable, efExtensions))
        {
            query = null!;
            return false;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(materializer);
        if (source is null
            || !EfQueryOperationAnalysis.TryAnalyzeSource(
                source,
                queryable,
                dbSet,
                dbContext,
                efExtensions,
                out var analysis)
            || !TryGetQueryIdentities(analysis.Origin, dbContext, out var contextIdentity, out var setIdentity))
        {
            query = null!;
            return false;
        }

        query = new QueryIdentity(analysis.Origin, contextIdentity, setIdentity);
        return true;
    }

    private static bool IsMaterializer(
        IInvocationOperation invocation,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol efExtensions)
    {
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var containingType = method.ContainingType.OriginalDefinition;
        return (SymbolEqualityComparer.Default.Equals(containingType, enumerable)
                && method.Name is "ToList" or "ToArray")
            || (SymbolEqualityComparer.Default.Equals(containingType, efExtensions)
                && method.Name is "ToListAsync" or "ToArrayAsync");
    }

    private static bool TryGetQueryIdentities(
        IOperation origin,
        INamedTypeSymbol dbContext,
        out OperationIdentity contextIdentity,
        out OperationIdentity? setIdentity)
    {
        origin = EfQueryOperationAnalysis.Unwrap(origin);
        if (origin is IPropertyReferenceOperation property
            && property.Instance is not null
            && TryCreateIdentity(property.Instance, out contextIdentity)
            && TryCreateIdentity(property, out var propertyIdentity))
        {
            setIdentity = propertyIdentity;
            return true;
        }

        if (origin is IInvocationOperation invocation
            && invocation.TargetMethod.Name == "Set"
            && EfQueryOperationAnalysis.IsOrDerivesFrom(invocation.TargetMethod.ContainingType, dbContext))
        {
            var source = invocation.Instance
                ?? invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)?.Value;
            if (source is not null
                && TryCreateIdentity(source, out contextIdentity)
                && TryCreateIdentity(invocation, out var invocationIdentity))
            {
                setIdentity = invocationIdentity;
                return true;
            }
        }

        contextIdentity = null!;
        setIdentity = null;
        return false;
    }

    private static bool TryGetFollowingSave(
        IForEachLoopOperation loop,
        IBlockOperation block,
        INamedTypeSymbol dbContext,
        OperationIdentity contextIdentity,
        out IMethodSymbol saveMethod,
        out bool asynchronous)
    {
        var loopIndex = IndexOf(block.Operations, loop);
        if (loopIndex < 0 || loopIndex + 1 >= block.Operations.Length)
        {
            saveMethod = null!;
            asynchronous = false;
            return false;
        }

        var operation = block.Operations[loopIndex + 1];
        if (operation is IExpressionStatementOperation expressionStatement)
        {
            operation = expressionStatement.Operation;
        }

        var awaited = operation is IAwaitOperation;
        operation = UnwrapAwait(operation);
        if (operation is not IInvocationOperation invocation
            || invocation.Instance is null
            || !IsSaveMethod(invocation.TargetMethod, dbContext)
            || !TryCreateIdentity(invocation.Instance, out var saveIdentity)
            || !contextIdentity.Equals(saveIdentity))
        {
            saveMethod = null!;
            asynchronous = false;
            return false;
        }

        asynchronous = invocation.TargetMethod.Name == "SaveChangesAsync";
        if (asynchronous != awaited)
        {
            saveMethod = null!;
            return false;
        }

        saveMethod = invocation.TargetMethod;
        return true;
    }

    private static bool TryClassifyUpdate(
        IOperation body,
        ILocalSymbol iterationLocal,
        out ImmutableArray<string> propertyNames)
    {
        var operations = GetBodyOperations(body);
        if (operations.IsDefaultOrEmpty)
        {
            propertyNames = default;
            return false;
        }

        var seen = new HashSet<IPropertySymbol>(SymbolEqualityComparer.Default);
        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var operation in operations)
        {
            var candidate = operation is IExpressionStatementOperation expressionStatement
                ? expressionStatement.Operation
                : operation;
            if (candidate is not ISimpleAssignmentOperation assignment
                || EfQueryOperationAnalysis.Unwrap(assignment.Target) is not IPropertyReferenceOperation property
                || property.Property.IsIndexer
                || property.Property.SetMethod is null
                || property.Instance is null
                || !IsIterationReference(property.Instance, iterationLocal)
                || !EfQueryOperationAnalysis.IsSimpleMappedProperty(property.Property)
                || !IsScalarPropertyType(property.Property.Type)
                || !IsStableAssignedValue(assignment.Value, iterationLocal)
                || !seen.Add(property.Property))
            {
                propertyNames = default;
                return false;
            }

            names.Add(property.Property.Name);
        }

        propertyNames = names.ToImmutable();
        return propertyNames.Length > 0;
    }

    private static bool TryClassifyDelete(
        IOperation body,
        ILocalSymbol iterationLocal,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol dbSet,
        OperationIdentity contextIdentity,
        OperationIdentity? setIdentity,
        out IMethodSymbol deleteMethod)
    {
        var operations = GetBodyOperations(body);
        if (operations.Length != 1)
        {
            deleteMethod = null!;
            return false;
        }

        var operation = operations[0] is IExpressionStatementOperation expressionStatement
            ? expressionStatement.Operation
            : operations[0];
        if (operation is not IInvocationOperation invocation
            || invocation.TargetMethod.Name != "Remove"
            || invocation.Instance is null
            || invocation.Arguments.Length != 1
            || !IsIterationReference(invocation.Arguments[0].Value, iterationLocal)
            || !TryCreateIdentity(invocation.Instance, out var receiverIdentity))
        {
            deleteMethod = null!;
            return false;
        }

        var methodOwner = invocation.TargetMethod.ContainingType;
        var matchesContext = EfQueryOperationAnalysis.IsOrDerivesFrom(methodOwner, dbContext)
            && contextIdentity.Equals(receiverIdentity);
        var matchesSet = EfQueryOperationAnalysis.IsOrDerivesFrom(methodOwner, dbSet)
            && setIdentity is not null
            && setIdentity.Equals(receiverIdentity);
        if (!matchesContext && !matchesSet)
        {
            deleteMethod = null!;
            return false;
        }

        deleteMethod = invocation.TargetMethod;
        return true;
    }

    private static ImmutableArray<IOperation> GetBodyOperations(IOperation body)
    {
        body = EfQueryOperationAnalysis.Unwrap(body);
        return body is IBlockOperation block ? block.Operations : ImmutableArray.Create(body);
    }

    private static bool IsStableAssignedValue(IOperation operation, ILocalSymbol iterationLocal)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (ContainsIterationReference(operation, iterationLocal))
        {
            return false;
        }

        return operation.ConstantValue.HasValue
            || operation is IDefaultValueOperation
            || operation is IParameterReferenceOperation
            || operation is ILocalReferenceOperation
            || operation is IFieldReferenceOperation;
    }

    private static bool IsScalarPropertyType(ITypeSymbol type)
    {
        if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol nullable)
        {
            type = nullable.TypeArguments.FirstOrDefault() ?? type;
        }

        return type.SpecialType != SpecialType.None
            || type.TypeKind == TypeKind.Enum
            || type.IsValueType
            || type is IArrayTypeSymbol array && array.ElementType.SpecialType == SpecialType.System_Byte;
    }

    private static bool ContainsIterationReference(IOperation operation, ILocalSymbol iterationLocal) =>
        DescendantsAndSelf(operation).Any(candidate => IsIterationReference(candidate, iterationLocal));

    private static bool IsIterationReference(IOperation operation, ILocalSymbol iterationLocal) =>
        EfQueryOperationAnalysis.Unwrap(operation) is ILocalReferenceOperation reference
        && SymbolEqualityComparer.Default.Equals(reference.Local, iterationLocal);

    private static bool IsSaveMethod(IMethodSymbol method, INamedTypeSymbol dbContext)
    {
        if (method.IsStatic || method.Name is not ("SaveChanges" or "SaveChangesAsync"))
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

    private static bool HasBulkMethod(ImmutableArray<IMethodSymbol> methods, string name) =>
        methods.Any(method => method.Name == name);

    private static IOperation UnwrapAwait(IOperation operation)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        if (operation is IAwaitOperation awaitOperation)
        {
            return EfQueryOperationAnalysis.Unwrap(awaitOperation.Operation);
        }

        return operation;
    }

    private static int IndexOf(ImmutableArray<IOperation> operations, IOperation target)
    {
        for (var index = 0; index < operations.Length; index++)
        {
            if (ReferenceEquals(operations[index], target))
            {
                return index;
            }
        }

        return -1;
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

    private static bool TryCreateIdentity(IOperation operation, out OperationIdentity identity)
    {
        var symbols = ImmutableArray.CreateBuilder<ISymbol>();
        if (TryAppendIdentity(EfQueryOperationAnalysis.Unwrap(operation), symbols))
        {
            identity = new OperationIdentity(symbols.ToImmutable());
            return true;
        }

        identity = null!;
        return false;
    }

    private static bool TryAppendIdentity(IOperation operation, ImmutableArray<ISymbol>.Builder symbols)
    {
        operation = EfQueryOperationAnalysis.Unwrap(operation);
        switch (operation)
        {
            case ILocalReferenceOperation local:
                symbols.Add(local.Local);
                return true;
            case IParameterReferenceOperation parameter:
                symbols.Add(parameter.Parameter);
                return true;
            case IInstanceReferenceOperation instance when instance.Type is not null:
                symbols.Add(instance.Type);
                return true;
            case IFieldReferenceOperation field:
                if (field.Instance is not null && !TryAppendIdentity(field.Instance, symbols))
                {
                    return false;
                }

                symbols.Add(field.Field);
                return true;
            case IPropertyReferenceOperation property:
                if (property.Instance is not null && !TryAppendIdentity(property.Instance, symbols))
                {
                    return false;
                }

                symbols.Add(property.Property);
                return true;
            case IInvocationOperation invocation when invocation.TargetMethod.Name == "Set":
                var source = invocation.Instance
                    ?? invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 0)?.Value;
                if (source is null || !TryAppendIdentity(source, symbols))
                {
                    return false;
                }

                symbols.Add(invocation.TargetMethod);
                return true;
            default:
                return false;
        }
    }

    private sealed class QueryIdentity
    {
        public QueryIdentity(IOperation origin, OperationIdentity contextIdentity, OperationIdentity? setIdentity)
        {
            Origin = origin;
            ContextIdentity = contextIdentity;
            SetIdentity = setIdentity;
        }

        public IOperation Origin { get; }

        public OperationIdentity ContextIdentity { get; }

        public OperationIdentity? SetIdentity { get; }
    }

    private sealed class OperationIdentity
    {
        private readonly ImmutableArray<ISymbol> symbols;

        public OperationIdentity(ImmutableArray<ISymbol> symbols) => this.symbols = symbols;

        public override bool Equals(object? obj)
        {
            if (obj is not OperationIdentity other || symbols.Length != other.symbols.Length)
            {
                return false;
            }

            for (var index = 0; index < symbols.Length; index++)
            {
                if (!SymbolEqualityComparer.Default.Equals(symbols[index], other.symbols[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var symbol in symbols)
            {
                hash = (hash * 31) + SymbolEqualityComparer.Default.GetHashCode(symbol);
            }

            return hash;
        }
    }
}
