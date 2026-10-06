using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingForeignKeyIndexAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD003";
    public const string RuleTitle = "Foreign key is missing a covering index";
    public const string Confidence = "high";
    public const string Impact = "A foreign key without a covering index can require additional scans or work for joins and referential updates or deletes.";
    public const string DocumentationKey = "EFD003";

    private const string ModelSnapshotMetadataName = "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot";
    private const string Message = "Foreign key '{0}' on entity '{1}' has no covering index or key in the current EF model snapshot";
    private const string BuilderNamespace = "Microsoft.EntityFrameworkCore.Metadata.Builders";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Performance",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports foreign keys in a supported EF Core relational model snapshot that lack a covering index or key.",
        helpLinkUri: EfHelpLinks.For(DiagnosticId));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    private static void StartCompilation(CompilationStartAnalysisContext context)
    {
        if (!EfAnalysisScope.Includes(context.Options, context.Compilation))
        {
            return;
        }

        var modelSnapshot = context.Compilation.GetTypeByMetadataName(ModelSnapshotMetadataName);
        var providerDetection = EfProviderDetection.Detect(context.Compilation);
        if (modelSnapshot is null || providerDetection.EligibleProvider is null)
        {
            return;
        }

        context.RegisterOperationBlockAction(operationContext =>
            AnalyzeOperationBlock(operationContext, modelSnapshot, providerDetection.EligibleProvider));
    }

    private static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        INamedTypeSymbol modelSnapshot,
        EfProviderDetection.ProviderInfo provider)
    {
        if (context.OwningSymbol is not IMethodSymbol method || !IsSnapshotBuildModel(method, modelSnapshot))
        {
            return;
        }

        var collector = new SnapshotCollector();
        foreach (var operationBlock in context.OperationBlocks)
        {
            collector.Visit(operationBlock);
        }

        var snapshotName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        foreach (var foreignKey in collector.ForeignKeys)
        {
            if (IsCoveredOrInconclusive(foreignKey, collector.Entities))
            {
                continue;
            }

            var properties = string.Join(", ", foreignKey.Properties);
            var evidence = $"Model snapshot '{snapshotName}' for provider '{provider.Name}' ({provider.Identity}) defines foreign key [{properties}] on dependent entity '{foreignKey.EntityName}', and no covering index or key is represented for that entity.";
            var remediation = "Review the model and, where appropriate, add HasIndex (or an Index attribute), generate a migration, and review the generated SQL. An index can be intentionally omitted for small tables, write-heavy workloads, deliberate index tradeoffs, or when an equivalent index is managed outside EF migrations; verify that evidence before suppressing EFD003.";
            var diagnosticProperties = ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.Confidence, Confidence)
                .Add(DiagnosticPropertyNames.Evidence, evidence)
                .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
                .Add(DiagnosticPropertyNames.SuggestedRemediation, remediation)
                .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

            context.ReportDiagnostic(EfDiagnostic.Create(
                Rule,
                foreignKey.Invocation.Syntax.GetLocation(),
                diagnosticProperties,
                properties,
                foreignKey.EntityName));
        }
    }

    private static bool IsSnapshotBuildModel(IMethodSymbol method, INamedTypeSymbol modelSnapshot)
    {
        if (method.Name != "BuildModel" || !method.IsOverride)
        {
            return false;
        }

        for (var candidate = method.ContainingType; candidate is not null; candidate = candidate.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, modelSnapshot))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCoveredOrInconclusive(
        ForeignKeyRecord foreignKey,
        IReadOnlyDictionary<string, EntityRecord> entities)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var entityName = foreignKey.EntityName;
        while (true)
        {
            if (!visited.Add(entityName) || !entities.TryGetValue(entityName, out var entity) || entity.IsCoverageInconclusive)
            {
                return true;
            }

            if (entity.CoveringPropertySets.Any(properties => IsLeadingPrefix(foreignKey.Properties, properties)))
            {
                return true;
            }

            if (entity.BaseEntityName is null)
            {
                return false;
            }

            entityName = entity.BaseEntityName;
        }
    }

    private static bool IsLeadingPrefix(ImmutableArray<string> foreignKey, ImmutableArray<string> candidate)
    {
        if (candidate.Length < foreignKey.Length)
        {
            return false;
        }

        for (var index = 0; index < foreignKey.Length; index++)
        {
            if (!string.Equals(foreignKey[index], candidate[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class SnapshotCollector : OperationWalker
    {
        private readonly Dictionary<string, EntityRecord> _entities = new(StringComparer.Ordinal);
        private readonly List<ForeignKeyRecord> _foreignKeys = new();

        public IReadOnlyDictionary<string, EntityRecord> Entities => _entities;

        public IReadOnlyList<ForeignKeyRecord> ForeignKeys => _foreignKeys;

        public override void VisitInvocation(IInvocationOperation operation)
        {
            base.VisitInvocation(operation);

            if (!IsEfBuilderMethod(operation.TargetMethod) || !TryGetContainingEntity(operation, out var containingEntity))
            {
                return;
            }

            switch (operation.TargetMethod.Name)
            {
                case "HasForeignKey":
                    CollectForeignKey(operation, containingEntity);
                    break;
                case "HasIndex":
                case "HasKey":
                case "HasAlternateKey":
                    CollectCoverage(operation, containingEntity);
                    break;
                case "HasBaseType":
                    CollectBaseType(operation, containingEntity);
                    break;
            }
        }

        private void CollectForeignKey(IInvocationOperation operation, string containingEntity)
        {
            var dependentEntity = TryGetDependentEntity(operation, out var explicitDependent)
                ? explicitDependent
                : containingEntity;
            if (!TryGetPropertySequence(operation, out var properties))
            {
                return;
            }

            GetOrCreateEntity(dependentEntity);
            _foreignKeys.Add(new ForeignKeyRecord(dependentEntity, properties, operation));
        }

        private void CollectCoverage(IInvocationOperation operation, string entityName)
        {
            var entity = GetOrCreateEntity(entityName);
            if (TryGetPropertySequence(operation, out var properties))
            {
                entity.CoveringPropertySets.Add(properties);
            }
            else
            {
                entity.IsCoverageInconclusive = true;
            }
        }

        private void CollectBaseType(IInvocationOperation operation, string entityName)
        {
            var entity = GetOrCreateEntity(entityName);
            var argument = operation.Arguments.FirstOrDefault(static candidate =>
                candidate.Parameter?.Name is "name" or "entityTypeName" or "baseTypeName" or "baseType");
            if (argument is null || !TryGetEntityIdentity(argument.Value, out var baseEntity))
            {
                entity.IsCoverageInconclusive = true;
                return;
            }

            entity.BaseEntityName = baseEntity;
        }

        private EntityRecord GetOrCreateEntity(string entityName)
        {
            if (!_entities.TryGetValue(entityName, out var entity))
            {
                entity = new EntityRecord();
                _entities.Add(entityName, entity);
            }

            return entity;
        }
    }

    private static bool IsEfBuilderMethod(IMethodSymbol method)
    {
        var candidate = (method.ReducedFrom ?? method).OriginalDefinition;
        return candidate.ContainingNamespace.ToDisplayString() == BuilderNamespace
            && candidate.ContainingAssembly.Name == "Microsoft.EntityFrameworkCore";
    }

    private static bool TryGetContainingEntity(IOperation operation, out string entityName)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IInvocationOperation invocation
                && invocation.TargetMethod.Name == "Entity"
                && invocation.TargetMethod.ContainingType.Name == "ModelBuilder"
                && invocation.TargetMethod.ContainingNamespace.ToDisplayString() == "Microsoft.EntityFrameworkCore"
                && invocation.TargetMethod.ContainingAssembly.Name == "Microsoft.EntityFrameworkCore")
            {
                if (invocation.TargetMethod.IsGenericMethod && invocation.TargetMethod.TypeArguments.Length == 1)
                {
                    entityName = invocation.TargetMethod.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    return true;
                }

                var nameArgument = invocation.Arguments.FirstOrDefault(static argument =>
                    argument.Parameter?.Name is "name" or "entityTypeName");
                if (nameArgument is not null && TryGetEntityIdentity(nameArgument.Value, out entityName))
                {
                    return true;
                }
            }
        }

        entityName = string.Empty;
        return false;
    }

    private static bool TryGetDependentEntity(IInvocationOperation operation, out string entityName)
    {
        var argument = operation.Arguments.FirstOrDefault(static candidate =>
            candidate.Parameter?.Name is "dependentEntityTypeName" or "dependentEntityType");
        if (argument is not null && TryGetEntityIdentity(argument.Value, out entityName))
        {
            return true;
        }

        if (operation.TargetMethod.IsGenericMethod && operation.TargetMethod.TypeArguments.Length > 0)
        {
            entityName = operation.TargetMethod.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return true;
        }

        entityName = string.Empty;
        return false;
    }

    private static bool TryGetEntityIdentity(IOperation operation, out string entityName)
    {
        operation = Unwrap(operation);
        if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string constantName)
        {
            entityName = constantName;
            return true;
        }

        if (operation is ITypeOfOperation typeOfOperation)
        {
            entityName = typeOfOperation.TypeOperand.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            return true;
        }

        entityName = string.Empty;
        return false;
    }

    private static bool TryGetPropertySequence(IInvocationOperation operation, out ImmutableArray<string> properties)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var foundPropertyParameter = false;
        foreach (var argument in operation.Arguments)
        {
            var parameterName = argument.Parameter?.Name;
            if (parameterName is null || !IsPropertyParameter(parameterName))
            {
                continue;
            }

            foundPropertyParameter = true;
            if (!TryAppendProperties(argument.Value, builder))
            {
                properties = ImmutableArray<string>.Empty;
                return false;
            }
        }

        properties = builder.ToImmutable();
        return foundPropertyParameter && properties.Length > 0;
    }

    private static bool IsPropertyParameter(string parameterName)
    {
        if (parameterName.IndexOf("principal", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return parameterName.IndexOf("propert", StringComparison.OrdinalIgnoreCase) >= 0
            || parameterName.Equals("foreignKeyExpression", StringComparison.Ordinal)
            || parameterName.Equals("indexExpression", StringComparison.Ordinal)
            || parameterName.Equals("keyExpression", StringComparison.Ordinal);
    }

    private static bool TryAppendProperties(IOperation operation, ImmutableArray<string>.Builder properties)
    {
        operation = Unwrap(operation);
        if (operation.ConstantValue.HasValue && operation.ConstantValue.Value is string propertyName)
        {
            properties.Add(propertyName);
            return true;
        }

        switch (operation)
        {
            case IArrayCreationOperation arrayCreation when arrayCreation.Initializer is not null:
                return TryAppendProperties(arrayCreation.Initializer, properties);
            case IArrayInitializerOperation initializer:
                foreach (var element in initializer.ElementValues)
                {
                    if (!TryAppendProperties(element, properties))
                    {
                        return false;
                    }
                }

                return true;
            case IDelegateCreationOperation delegateCreation:
                return TryAppendProperties(delegateCreation.Target, properties);
            case IAnonymousFunctionOperation anonymousFunction:
                return TryAppendLambdaBody(anonymousFunction.Body, properties);
            case IPropertyReferenceOperation propertyReference:
                properties.Add(propertyReference.Property.Name);
                return true;
            case IAnonymousObjectCreationOperation anonymousObject:
                foreach (var initializer in anonymousObject.Initializers)
                {
                    if (!TryAppendProperties(initializer, properties))
                    {
                        return false;
                    }
                }

                return true;
            case ISimpleAssignmentOperation assignment:
                return TryAppendProperties(assignment.Value, properties);
            default:
                return false;
        }
    }

    private static bool TryAppendLambdaBody(IBlockOperation body, ImmutableArray<string>.Builder properties)
    {
        var returnOperation = body.Operations.OfType<IReturnOperation>().SingleOrDefault();
        return returnOperation?.ReturnedValue is not null
            && TryAppendProperties(returnOperation.ReturnedValue, properties);
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private sealed class EntityRecord
    {
        public List<ImmutableArray<string>> CoveringPropertySets { get; } = new();

        public string? BaseEntityName { get; set; }

        public bool IsCoverageInconclusive { get; set; }
    }

    private sealed class ForeignKeyRecord
    {
        public ForeignKeyRecord(
            string entityName,
            ImmutableArray<string> properties,
            IInvocationOperation invocation)
        {
            EntityName = entityName;
            Properties = properties;
            Invocation = invocation;
        }

        public string EntityName { get; }

        public ImmutableArray<string> Properties { get; }

        public IInvocationOperation Invocation { get; }
    }
}
