using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers;

// Whether EF Core splits queries by default: `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)`
// in the context's configuration. The call is recognized by its syntax, wherever it occurs in the
// compilation. Configuration in another project can't be seen here, so the CLI, which sees the whole
// scan, and a user's .globalconfig can say so through a global option.
public static class EfSplitQueryConfiguration
{
    public const string SplitQueryByDefaultOption = "efdoctor.split_query_by_default";

    private const string ConfigurationMethod = "UseQuerySplittingBehavior";
    private const string SplitQueryMember = "SplitQuery";

    public static bool IsSetByOption(AnalyzerConfigOptions globalOptions) =>
        globalOptions.TryGetValue(SplitQueryByDefaultOption, out var value)
        && bool.TryParse(value, out var parsed)
        && parsed;

    public static bool IsConfiguredIn(Compilation compilation, CancellationToken cancellationToken) =>
        compilation.SyntaxTrees.Any(tree => ConfiguresSplitQuery(tree.GetRoot(cancellationToken)));

    public static bool ConfiguresSplitQuery(SyntaxNode root) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsSplitQueryConfiguration);

    private static bool IsSplitQueryConfiguration(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            _ => null,
        };
        if (name != ConfigurationMethod || invocation.ArgumentList.Arguments.Count != 1)
        {
            return false;
        }

        return invocation.ArgumentList.Arguments[0].Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == SplitQueryMember,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText == SplitQueryMember,
            _ => false,
        };
    }
}
