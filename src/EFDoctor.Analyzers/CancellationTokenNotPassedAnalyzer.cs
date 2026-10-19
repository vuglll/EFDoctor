using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CancellationTokenNotPassedAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "EFD016";
    public const string RuleTitle = "CancellationToken not passed to an EF Core async call";
    public const string Confidence = "advisory";
    public const string DocumentationKey = "EFD016";
    public const string Impact = "Without the token, cancelling the request or shutting down the host doesn't cancel the database command: it keeps a connection and database resources until it completes, although nothing waits for its result. Under load, abandoned commands pile up.";
    public const string Remediation = "Pass the token that is in scope, for example ToListAsync(cancellationToken) or SaveChangesAsync(cancellationToken). For FindAsync, use FindAsync(new object[] { key }, cancellationToken). Pass CancellationToken.None explicitly when the operation must complete even if the caller cancels.";

    private const string DbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    private const string DbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    private const string EfExtensionsMetadataName = "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions";
    private const string RelationalExtensionsMetadataName = "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions";
    private const string CancellationTokenMetadataName = "System.Threading.CancellationToken";
    private const string Message = "'{0}' doesn't pass the CancellationToken '{1}' that is in scope";

    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        RuleTitle,
        Message,
        "Reliability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports an EF Core asynchronous operation whose CancellationToken parameter is left at its default while a CancellationToken parameter is in scope.",
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

        var dbSet = context.Compilation.GetTypeByMetadataName(DbSetMetadataName);
        var dbContext = context.Compilation.GetTypeByMetadataName(DbContextMetadataName);
        var efExtensions = context.Compilation.GetTypeByMetadataName(EfExtensionsMetadataName);
        var cancellationToken = context.Compilation.GetTypeByMetadataName(CancellationTokenMetadataName);
        if (dbSet is null || dbContext is null || efExtensions is null || cancellationToken is null)
        {
            return;
        }

        var relationalExtensions = context.Compilation.GetTypeByMetadataName(RelationalExtensionsMetadataName);
        context.RegisterOperationAction(
            operationContext => AnalyzeInvocation(operationContext, dbSet, dbContext, efExtensions, relationalExtensions, cancellationToken),
            OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(
        OperationAnalysisContext context,
        INamedTypeSymbol dbSet,
        INamedTypeSymbol dbContext,
        INamedTypeSymbol efExtensions,
        INamedTypeSymbol? relationalExtensions,
        INamedTypeSymbol cancellationToken)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = EfQueryOperationAnalysis.NormalizeMethod(invocation.TargetMethod);
        var isRelational = relationalExtensions is not null
            && method.Name.EndsWith("Async", StringComparison.Ordinal)
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, relationalExtensions);
        if ((!EfAsyncOperations.IsCoreOperation(method, dbContext, dbSet, efExtensions) && !isRelational)
            || !MissesToken(invocation, cancellationToken)
            || FindTokenInScope(invocation, context.ContainingSymbol, cancellationToken) is not { } token)
        {
            return;
        }

        var evidence = $"Invocation resolves to '{invocation.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}', which accepts a CancellationToken but is called without one, while parameter '{token.Name}' of type CancellationToken is in scope.";
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.Confidence, Confidence)
            .Add(DiagnosticPropertyNames.Evidence, evidence)
            .Add(DiagnosticPropertyNames.LikelyImpact, Impact)
            .Add(DiagnosticPropertyNames.SuggestedRemediation, Remediation)
            .Add(DiagnosticPropertyNames.DocumentationReference, DocumentationKey);

        context.ReportDiagnostic(EfDiagnostic.Create(Rule, invocation.Syntax.GetLocation(), properties, invocation.TargetMethod.Name, token.Name));
    }

    // The token parameter is filled in with its default value, or FindAsync's params overload,
    // which has no token, was chosen.
    private static bool MissesToken(IInvocationOperation invocation, INamedTypeSymbol cancellationToken)
    {
        var tokenArgument = invocation.Arguments.FirstOrDefault(argument =>
            argument.Parameter is { } parameter && SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationToken));
        if (tokenArgument is not null)
        {
            return tokenArgument.ArgumentKind == ArgumentKind.DefaultValue;
        }

        return invocation.TargetMethod.Name == "FindAsync";
    }

    private static IParameterSymbol? FindTokenInScope(IOperation operation, ISymbol containingSymbol, INamedTypeSymbol cancellationToken)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            var parameters = current switch
            {
                IAnonymousFunctionOperation lambda => lambda.Symbol.Parameters,
                ILocalFunctionOperation local => local.Symbol.Parameters,
                _ => ImmutableArray<IParameterSymbol>.Empty,
            };
            if (parameters.FirstOrDefault(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationToken)) is { } found)
            {
                return found;
            }
        }

        return containingSymbol is IMethodSymbol method
            ? method.Parameters.FirstOrDefault(parameter => SymbolEqualityComparer.Default.Equals(parameter.Type, cancellationToken))
            : null;
    }
}
