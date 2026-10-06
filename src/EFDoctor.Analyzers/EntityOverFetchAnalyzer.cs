using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntityOverFetchAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD037";
    public const string RuleTitle = "Entities are materialized but only a few columns are read";
    public const string Confidence = "advisory";
    public const string DocumentationKey = "EFD037";
    public const string Impact = "Every mapped column of each row is read from the database, transferred, and materialized, and the entities are tracked unless the query is no-tracking, although the method uses only a few of the values. The actual cost depends on the number of rows and on the size of the unused columns.";

    // A finding needs at most half of the entity's scalar properties read, and this many unread.
    internal const int MinimumUnreadProperties = 4;

    private const string EnumerableMetadataName = "System.Linq.Enumerable";
    private const string QueryableMetadataName = "System.Linq.Queryable";
    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string Message = "This query loads complete '{0}' entities, but the method reads only {1} of their {2} scalar properties";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports an EF Core query materialized into a local when every use of the result in the method reads only a small subset of the entity's scalar properties.",
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
        if (!PrematureQueryMaterializationAnalyzer.TryClassifyMaterializer(materializer, enumerable, efExtensions, out var asynchronous)
            || !PrematureQueryMaterializationAnalyzer.TryGetMaterializedValue(materializer, asynchronous, out var value)
            || !TryGetInitializedLocal(value, out var local))
        {
            return;
        }

        var source = EfQueryOperationAnalysis.GetInvocationSource(materializer);
        if (source is null
            || !EfQueryOperationAnalysis.TryAnalyzeSource(source, queryable, dbSet, dbContext, efExtensions, out var queryAnalysis)
            || queryAnalysis.Operations.Any(static operation => operation is "EF.Include" or "EF.ThenInclude")
            || !TryGetEntityType(queryAnalysis.Origin, dbSet, out var entity)
            || !SymbolEqualityComparer.Default.Equals(GetElementType(local.Type), entity))
        {
            return;
        }

        var counted = CountedProperties(entity);
        var usage = new ResultUsage(local, counted, enumerable, Root(materializer));
        if (!usage.TryCollectReads(context.CancellationToken, out var reads)
            || reads.Count == 0
            || reads.Count * 2 > counted.Count
            || counted.Count - reads.Count < MinimumUnreadProperties)
        {
            return;
        }

        var names = reads.Select(static property => property.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        var entityName = entity.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var evidence = $"The query is traced to DbSet origin '{queryAnalysis.Origin.Syntax}' and materialized into local '{local.Name}'. Every use of '{local.Name}' and of its elements in this method is a read of a scalar property. The method reads {names.Length} of the {counted.Count} scalar properties of '{entityName}': {string.Join(", ", names)}.";
        var projection = string.Join(", ", names.Select(static name => "x." + name));
        var remediation = $"Project the values in the query, for example with Select(x => new {{ {projection} }}) or a small result type before the materializer, so that SQL returns only those columns and nothing is tracked. Keep the entity load when the entities are modified and saved, are needed whole by code this rule can't see, or come from a narrow table where the extra columns don't matter.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, materializer.Syntax.GetLocation(), properties, entityName, names.Length, counted.Count));
    }

    // The materialized value must initialize a local declared by a declaration statement, so
    // that every later use of the result goes through that one name.
    private static bool TryGetInitializedLocal(IOperation value, out ILocalSymbol local)
    {
        if (value.Parent is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }
            && declarator.Parent is IVariableDeclarationOperation { Parent: IVariableDeclarationGroupOperation })
        {
            local = declarator.Symbol;
            return true;
        }

        local = null!;
        return false;
    }

    internal static bool TryGetEntityType(IOperation origin, INamedTypeSymbol dbSet, out INamedTypeSymbol entity)
    {
        for (var type = origin.Type as INamedTypeSymbol; type is not null; type = type.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, dbSet)
                && type.TypeArguments[0] is INamedTypeSymbol argument)
            {
                entity = argument;
                return true;
            }
        }

        entity = null!;
        return false;
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol type) =>
        type switch
        {
            IArrayTypeSymbol array => array.ElementType,
            INamedTypeSymbol { TypeArguments.Length: 1 } named => named.TypeArguments[0],
            _ => null,
        };

    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent is not null)
        {
            operation = operation.Parent;
        }

        return operation;
    }

    // The entity's mapped scalar columns, approximated from the type: public read-write
    // auto-properties of a scalar type that aren't excluded from the model.
    internal static ImmutableHashSet<IPropertySymbol> CountedProperties(INamedTypeSymbol entity)
    {
        var counted = ImmutableHashSet.CreateBuilder<IPropertySymbol>(SymbolEqualityComparer.Default);
        for (var type = entity; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (var member in type.GetMembers())
            {
                if (member is IPropertySymbol property
                    && !property.IsStatic
                    && !property.IsIndexer
                    && !property.IsOverride
                    && property.DeclaredAccessibility == Accessibility.Public
                    && property.GetMethod is not null
                    && property.SetMethod is not null
                    && IsScalar(property.Type)
                    && EfQueryOperationAnalysis.IsSimpleMappedProperty(property)
                    && !property.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == "NotMappedAttribute"))
                {
                    counted.Add(property.OriginalDefinition);
                }
            }
        }

        return counted.ToImmutable();
    }

    private static bool IsScalar(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum
            || type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_String:
            case SpecialType.System_DateTime:
                return true;
        }

        return type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
            && type.Name is "DateTimeOffset" or "TimeSpan" or "DateOnly" or "TimeOnly" or "Guid";
    }

    // Checks that everything the method does with the result local, and with each element taken
    // from it, is a read of a counted property. Any other use means an entity may be needed whole.
    private sealed class ResultUsage
    {
        private readonly ILocalSymbol _local;
        private readonly ImmutableHashSet<IPropertySymbol> _counted;
        private readonly INamedTypeSymbol _enumerable;
        private readonly IOperation _root;
        private readonly HashSet<IPropertySymbol> _reads = new(SymbolEqualityComparer.Default);

        public ResultUsage(ILocalSymbol local, ImmutableHashSet<IPropertySymbol> counted, INamedTypeSymbol enumerable, IOperation root)
        {
            _local = local;
            _counted = counted;
            _enumerable = enumerable;
            _root = root;
        }

        public bool TryCollectReads(CancellationToken cancellationToken, out HashSet<IPropertySymbol> reads)
        {
            reads = _reads;
            foreach (var operation in _root.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (operation is ILocalReferenceOperation reference
                    && SymbolEqualityComparer.Default.Equals(reference.Local, _local)
                    && !IsSupportedUse(reference))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSupportedUse(IOperation reference)
        {
            var current = SkipWrappers(reference);
            switch (current.Parent)
            {
                case IForEachLoopOperation loop when loop.Collection == current:
                    return loop.LoopControlVariable is IVariableDeclaratorOperation variable
                        && AllReferences(_root, variable.Symbol).All(IsCountedRead);

                case IArgumentOperation { Parameter.Ordinal: 0, Parent: IInvocationOperation invocation }:
                    return IsSupportedEnumerableCall(invocation);

                case IPropertyReferenceOperation property when property.Instance == current:
                    if (property.Property.IsIndexer)
                    {
                        return IsCountedRead(property);
                    }

                    // List<T>.Count and array Length use no element.
                    return property.Arguments.Length == 0 && property.Property.Name is "Count" or "Length";

                case IArrayElementReferenceOperation element when element.ArrayReference == current:
                    return IsCountedRead(element);

                default:
                    return false;
            }
        }

        private bool IsSupportedEnumerableCall(IInvocationOperation invocation)
        {
            var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
            if (!SymbolEqualityComparer.Default.Equals(method.ContainingType, _enumerable))
            {
                return false;
            }

            if (invocation.Arguments.Length == 1)
            {
                return method.Name is "Any" or "Count";
            }

            if (invocation.Arguments.Length != 2
                || method.Name is not ("Select" or "Any" or "All" or "Count" or "Sum" or "Min" or "Max" or "Average"))
            {
                return false;
            }

            var argument = invocation.Arguments.FirstOrDefault(static argument => argument.Parameter?.Ordinal == 1);
            return argument is not null
                && EfQueryOperationAnalysis.TryGetLambda(argument.Value, out var lambda)
                && lambda.Symbol.Parameters.Length == 1
                && AllReferences(lambda.Body, lambda.Symbol.Parameters[0]).All(IsCountedRead);
        }

        private static IEnumerable<IOperation> AllReferences(IOperation scope, ISymbol element) =>
            scope.Descendants().Where(operation =>
                operation switch
                {
                    ILocalReferenceOperation local => SymbolEqualityComparer.Default.Equals(local.Local, element),
                    IParameterReferenceOperation parameter => SymbolEqualityComparer.Default.Equals(parameter.Parameter, element),
                    _ => false,
                });

        private bool IsCountedRead(IOperation element)
        {
            var current = SkipWrappers(element);
            if (current.Parent is not IPropertyReferenceOperation property
                || property.Instance != current
                || !_counted.Contains(property.Property.OriginalDefinition)
                || IsWritten(property))
            {
                return false;
            }

            _reads.Add(property.Property.OriginalDefinition);
            return true;
        }

        private static bool IsWritten(IOperation property)
        {
            var current = property;
            while (current.Parent is ITupleOperation or IParenthesizedOperation)
            {
                current = current.Parent;
            }

            return current.Parent switch
            {
                IAssignmentOperation assignment => assignment.Target == current,
                IIncrementOrDecrementOperation => true,
                IArgumentOperation argument => argument.Parameter is { RefKind: not RefKind.None },
                _ => false,
            };
        }

        private static IOperation SkipWrappers(IOperation operation)
        {
            while (operation.Parent is IParenthesizedOperation
                || (operation.Parent is IConversionOperation { IsImplicit: true } conversion && conversion.Operand == operation))
            {
                operation = operation.Parent;
            }

            return operation;
        }
    }
}
