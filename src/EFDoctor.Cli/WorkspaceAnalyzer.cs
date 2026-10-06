using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Xml.Linq;
using EFDoctor.Analyzers;
using EFDoctor.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;

namespace EFDoctor.Cli;

public static class WorkspaceAnalyzer
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzers =
        ImmutableArray.Create<DiagnosticAnalyzer>(
            new SaveChangesInLoopAnalyzer(),
            new CountUsedForExistenceAnalyzer(),
            new MissingForeignKeyIndexAnalyzer(),
            new PrematureQueryMaterializationAnalyzer(),
            new UnboundedQueryMaterializationAnalyzer(),
            new MultipleCollectionIncludeAnalyzer(),
            new SyncOverAsyncEfOperationAnalyzer(),
            new UnsafeRawSqlAnalyzer(),
            new BulkUpdateDeleteAnalyzer(),
            new ProjectionDropsIncludeAnalyzer(),
            new UnawaitedAsyncEfOperationAnalyzer(),
            new MaterializeThenReduceAnalyzer(),
            new UnorderedPaginationAnalyzer(),
            new RedundantIncludeAnalyzer(),
            new StaticDbContextFieldAnalyzer(),
            new NonSargableCaseTransformAnalyzer(),
            new StringComparisonInPredicateAnalyzer(),
            new MultipleEnumerationAnalyzer(),
            new LeadingWildcardSearchAnalyzer(),
            new SyncDatabaseCallInAsyncAnalyzer(),
            new OrderByReplacesOrderingAnalyzer(),
            new ConcurrentDbContextOperationAnalyzer(),
            new EntityOverFetchAnalyzer(),
            new StaleTrackedEntitiesAnalyzer());

    private static readonly ImmutableHashSet<string> DiagnosticIds = Analyzers
        .SelectMany(static analyzer => analyzer.SupportedDiagnostics)
        .Select(static descriptor => descriptor.Id)
        .ToImmutableHashSet(StringComparer.Ordinal);

    public static async Task<AnalysisResult> AnalyzeAsync(string targetPath, CancellationToken cancellationToken, bool includeTestProjects = false)
    {
        using var workspace = MSBuildWorkspace.Create(WorkspaceProperties());
        var failures = new ConcurrentQueue<string>();
        workspace.RegisterWorkspaceFailedHandler(eventArgs =>
        {
            if (eventArgs.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                failures.Enqueue(eventArgs.Diagnostic.Message);
            }
        });

        Solution solution;
        var extension = Path.GetExtension(targetPath);
        if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(targetPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            solution = project.Solution;
        }
        else
        {
            solution = await workspace.OpenSolutionAsync(targetPath, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // MSBuildWorkspace reports WorkspaceDiagnosticKind.Failure for many non-fatal
        // load conditions (build warnings, unresolved optional imports, analyzer or
        // generator load problems) while still producing a usable compilation. Load
        // diagnostics therefore do not fail the scan on their own; the run fails only
        // when no analyzable C# compilation is available.
        var findings = new List<Finding>();
        var unanalyzableEfProjects = new List<string>();
        var analyzableEfProjects = 0;
        var analyzedAnyCompilation = false;
        var skippedTestProject = false;
        foreach (var project in solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null || !compilation.SyntaxTrees.Any())
            {
                // A failed design-time build still yields a non-null but empty
                // compilation (no source documents); that is not something we can
                // analyze, so it does not count as a successfully loaded project.
                continue;
            }

            var isTestProject = IsTestProject(project, compilation);
            if (isTestProject && !includeTestProjects)
            {
                // Test projects are skipped by default; --include-test-projects opts
                // them back in. Skipping is intentional, not a load failure.
                skippedTestProject = true;
                continue;
            }

            analyzedAnyCompilation = true;
            if (UsesEntityFrameworkCore(compilation, cancellationToken))
            {
                if (compilation.GetTypeByMetadataName(DbContextMetadataName) is null)
                {
                    // Every rule needs EF Core types; without them the project would silently
                    // look clean, so say so instead of analyzing it.
                    unanalyzableEfProjects.Add(DescribeUnanalyzableProject(project, failures));
                    continue;
                }

                analyzableEfProjects++;
            }

            // The analyzers skip test projects on their own; an included one needs their opt-in.
            var analyzerOptions = new CompilationWithAnalyzersOptions(
                isTestProject ? TestProjectAnalyzerOptions.OptIn(project.AnalyzerOptions) : project.AnalyzerOptions,
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false);
            var diagnostics = await compilation
                .WithAnalyzers(Analyzers, analyzerOptions)
                .GetAnalyzerDiagnosticsAsync(cancellationToken)
                .ConfigureAwait(false);

            var projectFindings = diagnostics
                .Where(diagnostic => DiagnosticIds.Contains(diagnostic.Id) && !diagnostic.IsSuppressed)
                .Select(DiagnosticFindingMapper.Map);
            findings.AddRange(isTestProject
                ? projectFindings.Select(DowngradeToAdvisory)
                : projectFindings);
        }

        // Fail only when nothing analyzable loaded and nothing was intentionally
        // skipped: a target whose only projects are skipped test projects is a
        // clean, successful scan with no findings.
        if (!analyzedAnyCompilation && !skippedTestProject)
        {
            return AnalysisResult.Failure(failures.IsEmpty
                ? "Could not create a C# compilation for the target."
                : "MSBuild could not load the target: " + string.Join(" | ", failures));
        }

        if (unanalyzableEfProjects.Count > 0 && analyzableEfProjects == 0)
        {
            return AnalysisResult.Failure(
                "No project that uses EF Core could be analyzed. " + string.Join(" ", unanalyzableEfProjects));
        }

        var distinct = findings
            .GroupBy(static finding => new
            {
                Path = FindingOrder.NormalizePath(finding.SourceFile),
                finding.Range.StartLine,
                finding.Range.StartColumn,
                finding.Range.EndLine,
                finding.Range.EndColumn,
                finding.RuleId,
            })
            .Select(static group => group.First());

        return AnalysisResult.Success(distinct, unanalyzableEfProjects);
    }

    private const string WorkspaceTargetsFile = "build/EFDoctor.Workspace.targets";

    // Roslyn's build host splits TargetFrameworks on ';' without trimming, so a list written across
    // several lines fails to load. The targets file normalizes it in the outer, cross-targeting
    // evaluation; a missing file (a broken install) just loads without the fix.
    private static Dictionary<string, string> WorkspaceProperties()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetsPath = Path.Combine(AppContext.BaseDirectory, WorkspaceTargetsFile);
        if (File.Exists(targetsPath))
        {
            properties["CustomAfterMicrosoftCommonCrossTargetingTargets"] = targetsPath;
        }

        return properties;
    }

    private const string DbContextMetadataName ="Microsoft.EntityFrameworkCore.DbContext";
    private const string EfCoreNamespace = "Microsoft.EntityFrameworkCore";

    // Read from source rather than references: when EF Core fails to resolve, the compilation may
    // have no references at all, but the using directives are still there.
    private static bool UsesEntityFrameworkCore(Compilation compilation, CancellationToken cancellationToken) =>
        compilation.SyntaxTrees.Any(tree => tree.GetRoot(cancellationToken)
            .DescendantNodes(static node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax)
            .OfType<UsingDirectiveSyntax>()
            .Any(static directive => directive.Name?.ToString() is { } name
                && (name == EfCoreNamespace || name.StartsWith(EfCoreNamespace + ".", StringComparison.Ordinal))));

    private static string DescribeUnanalyzableProject(Project project, IEnumerable<string> loadFailures)
    {
        var description = $"Project '{project.Name}' uses EF Core, but EF Core types could not be resolved in its compilation, so it was not analyzed. Restore and build the project, then run EFDoctor again.";
        var projectDirectory = Path.GetDirectoryName(project.FilePath);
        var related = loadFailures
            .Where(message => projectDirectory is not null && message.Contains(projectDirectory, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .Select(static message => message.Length > 300 ? message[..300] + "…" : message)
            .ToList();
        return related.Count == 0
            ? description
            : description + " Load diagnostics: " + string.Join(" | ", related);
    }

    private static bool IsTestProject(Project project, Compilation compilation)
    {
        return EfTestProjectDetection.IsMarkedTestProject(project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions)
            || IsMarkedTestProject(project.FilePath)
            || EfTestProjectDetection.ReferencesTestFramework(compilation);
    }

    private static bool IsMarkedTestProject(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return false;
        }

        try
        {
            return XDocument.Load(projectFilePath)
                .Descendants()
                .Any(element => element.Name.LocalName == "IsTestProject"
                    && bool.TryParse(element.Value, out var markedAsTest)
                    && markedAsTest);
        }
        catch (IOException)
        {
            return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static Finding DowngradeToAdvisory(Finding finding)
    {
        return new Finding(
            finding.RuleId,
            finding.RuleTitle,
            FindingSeverity.Info,
            FindingConfidence.Advisory,
            finding.Message,
            finding.SourceFile,
            finding.Range,
            finding.Evidence,
            finding.LikelyImpact,
            finding.SuggestedRemediation,
            finding.DocumentationReference);
    }
}
