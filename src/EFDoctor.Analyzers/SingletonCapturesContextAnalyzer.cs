using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingletonCapturesContextAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD033";
    public const string RuleTitle = "DbContext captured by a singleton";
    public const string Confidence = "high";
    public const string DocumentationKey = "EFD033";
    public const string Impact = "AddDbContext registers a DbContext as scoped. A singleton or hosted service that takes one captures a single instance for the application's lifetime. With scope validation, the default in Development, the host fails to start; in Production every request shares that DbContext, which is not thread-safe, so concurrent use throws \"A second operation was started on this context instance\", tracked entities go stale, and the change tracker grows without bound.";
    public const string Remediation = "Take IDbContextFactory<TContext> (registered with AddDbContextFactory) and create a short-lived context per operation, or take IServiceScopeFactory and resolve the context inside a scope per unit of work, or register the service as scoped if it doesn't need to be a singleton.";

    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string ServiceCollectionMetadataName = "Microsoft.Extensions.DependencyInjection.IServiceCollection";
    private const string Message = "Singleton '{0}' captures DbContext '{1}' for the application's lifetime";

    private static readonly ImmutableHashSet<string> SingletonRegistrations =
        ImmutableHashSet.Create(StringComparer.Ordinal, "AddSingleton", "TryAddSingleton", "AddHostedService");

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Reliability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports a singleton or hosted-service registration whose implementation takes a DbContext in every public constructor, or whose factory resolves a DbContext from the root service provider.",
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
        var serviceCollection = context.Compilation.GetTypeByMetadataName(ServiceCollectionMetadataName);
        if (dbContext is null || serviceCollection is null)
        {
            return;
        }

        var compilation = context.Compilation;
        var singletonContexts = new Lazy<ImmutableHashSet<string>>(
            () => CollectSingletonContexts(compilation, context.CancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, serviceCollection, dbContext, singletonContexts),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol serviceCollection,
        INamedTypeSymbol dbContext,
        Lazy<ImmutableHashSet<string>> singletonContexts)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!SingletonRegistrations.Contains(method.Name) || !IsServiceCollection(EfQueryOperationAnalysis.GetInvocationSource(invocation)?.Type, serviceCollection))
        {
            return;
        }

        if (!TryFindCapture(invocation, dbContext, out var implementation, out var captured, out var how)
            || singletonContexts.Value.Contains(captured.Name))
        {
            return;
        }

        var evidence = $"'{invocation.Syntax}' registers '{implementation}' with a singleton lifetime, and {how}. AddDbContext registers '{captured.Name}' as scoped by default, so the singleton keeps one instance for the application's lifetime.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, implementation, captured.Name));
    }

    private static bool TryFindCapture(IInvocationOperation invocation, INamedTypeSymbol dbContext, out string implementation, out ITypeSymbol captured, out string how)
    {
        implementation = null!;
        captured = null!;
        how = null!;

        var arguments = invocation.Arguments
            .Where(argument => !(invocation.TargetMethod.IsExtensionMethod && invocation.Instance is null && argument.Parameter?.Ordinal == 0))
            .Select(static argument => EfQueryOperationAnalysis.Unwrap(argument.Value))
            .ToList();

        foreach (var argument in arguments)
        {
            if (EfQueryOperationAnalysis.TryGetLambda(argument, out var lambda)
                && lambda.Symbol.Parameters.Length == 1
                && TryFindFactoryResolution(lambda, dbContext, out captured))
            {
                implementation = invocation.TargetMethod.TypeArguments.FirstOrDefault()?.Name ?? "factory";
                how = $"its factory resolves '{captured.Name}' from the root service provider '{lambda.Symbol.Parameters[0].Name}'";
                return true;
            }
        }

        var type = arguments.OfType<ITypeOfOperation>().LastOrDefault()?.TypeOperand
            ?? invocation.TargetMethod.TypeArguments.LastOrDefault();
        if (type is not INamedTypeSymbol named
            || !named.Locations.Any(static location => location.IsInSource)
            || named.IsAbstract)
        {
            return false;
        }

        var constructors = named.InstanceConstructors.Where(static constructor => constructor.DeclaredAccessibility == Accessibility.Public).ToList();
        if (constructors.Count == 0)
        {
            return false;
        }

        IParameterSymbol? parameter = null;
        foreach (var constructor in constructors)
        {
            var contextParameter = constructor.Parameters.FirstOrDefault(candidate => EfQueryOperationAnalysis.IsOrDerivesFrom(candidate.Type, dbContext));
            if (contextParameter is null)
            {
                return false;
            }

            parameter ??= contextParameter;
        }

        implementation = named.Name;
        captured = parameter!.Type;
        how = $"its constructor takes '{captured.Name} {parameter.Name}'";
        return true;
    }

    // `sp.GetRequiredService<TContext>()`, `sp.GetService<TContext>()`, or the `typeof` form, called
    // directly on the factory's own provider parameter rather than on a scope it creates.
    private static bool TryFindFactoryResolution(IAnonymousFunctionOperation lambda, INamedTypeSymbol dbContext, out ITypeSymbol captured)
    {
        captured = null!;
        var provider = lambda.Symbol.Parameters[0];
        foreach (var resolve in lambda.Body.Descendants().OfType<IInvocationOperation>())
        {
            if (resolve.TargetMethod.Name is not ("GetRequiredService" or "GetService")
                || EfQueryOperationAnalysis.GetInvocationSource(resolve) is not { } source
                || EfQueryOperationAnalysis.Unwrap(source) is not IParameterReferenceOperation reference
                || !SymbolEqualityComparer.Default.Equals(reference.Parameter, provider))
            {
                continue;
            }

            var resolved = resolve.TargetMethod.TypeArguments.FirstOrDefault()
                ?? resolve.Arguments.Select(static argument => EfQueryOperationAnalysis.Unwrap(argument.Value)).OfType<ITypeOfOperation>().FirstOrDefault()?.TypeOperand;
            if (resolved is not null && EfQueryOperationAnalysis.IsOrDerivesFrom(resolved, dbContext))
            {
                captured = resolved;
                return true;
            }
        }

        return false;
    }

    private static bool IsServiceCollection(ITypeSymbol? type, INamedTypeSymbol serviceCollection) =>
        type is not null
        && (SymbolEqualityComparer.Default.Equals(type, serviceCollection)
            || type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, serviceCollection)));

    // Names of context types the compilation registers with a singleton lifetime, which a
    // singleton may then take consistently: `AddDbContext<T>(..., ServiceLifetime.Singleton)`, the
    // pooled form, or `AddSingleton<T>()`. The scan is syntactic: it only ever removes findings.
    private static ImmutableHashSet<string> CollectSingletonContexts(Compilation compilation, CancellationToken cancellationToken)
    {
        var names = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var syntax in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var generic = syntax.Expression switch
                {
                    MemberAccessExpressionSyntax { Name: GenericNameSyntax name } => name,
                    GenericNameSyntax name => name,
                    _ => null,
                };
                if (generic is null || generic.TypeArgumentList.Arguments.Count == 0)
                {
                    continue;
                }

                var method = generic.Identifier.ValueText;
                var singleton = method is "AddSingleton" or "TryAddSingleton"
                    || (method is "AddDbContext" or "AddDbContextPool"
                        && syntax.ArgumentList.Arguments.Any(static argument => argument.Expression.ToString().EndsWith("ServiceLifetime.Singleton", StringComparison.Ordinal)));
                if (singleton)
                {
                    names.Add(RightmostName(generic.TypeArgumentList.Arguments.Last()));
                }
            }
        }

        return names.ToImmutable();
    }

    private static string RightmostName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => type.ToString(),
    };
}
