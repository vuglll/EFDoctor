using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using EFDoctor.Analyzers;
using EFDoctor.Cli;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Cli.Tests;

// Keeps analyzers, CLI registration, specs, rule documentation, release metadata, the
// solution, and scenario-level test coverage in sync (openspec/specs/repository-consistency).
public sealed class RepositoryConsistencyTests
{
    private static readonly Regex RuleIdPattern = new(@"EFD\d{3}", RegexOptions.Compiled);
    private static readonly Regex RuleRangePattern = new(@"EFD(\d{3}) through EFD(\d{3})", RegexOptions.Compiled);
    private static readonly Regex SpecTraitPattern = new(@"\[Trait\(\s*""Spec""\s*,\s*""([^""]+)""\s*\)\]", RegexOptions.Compiled);
    private static readonly Regex ScenarioPattern = new(@"^#### Scenario: (.+?)\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly string Root = RepositoryRoot();

    [Fact]
    public void ShippedRulesAreRegisteredInTheCli()
    {
        var registered = RegisteredRuleIds();
        var problems = ShippedRuleIds()
            .Where(id => !registered.Contains(id))
            .Select(static id => $"{id}: shipped by the analyzer assembly but not registered in WorkspaceAnalyzer")
            .ToList();

        AssertNoProblems(problems);
    }

    [Fact]
    public void ShippedRulesHaveSpecsAndDocumentation()
    {
        var readme = Read("README.md");
        var package = Read("src/EFDoctor.Cli/Package/PACKAGE.md");
        var changelog = Read("CHANGELOG.md");
        var releases = Read("src/EFDoctor.Analyzers/AnalyzerReleases.Shipped.md") + "\n" + Read("src/EFDoctor.Analyzers/AnalyzerReleases.Unshipped.md");
        var statusCoverage = Coverage(SingleLine(readme, "> **Project status:**"));
        var verificationCoverage = Coverage(SingleLine(readme, "- `tests/EFDoctor.Analyzers.Tests` covers"));
        var specFolders = SpecCapabilities();

        var problems = new List<string>();
        foreach (var id in ShippedRuleIds())
        {
            Require(specFolders.Any(folder => folder.StartsWith(id.ToLowerInvariant() + "-", StringComparison.Ordinal)), id, "a rule spec folder under openspec/specs/");
            Require(File.Exists(Path.Combine(Root, "docs", "rules", $"{id}.md")), id, $"docs/rules/{id}.md");
            Require(readme.Contains($"| **{id}** |", StringComparison.Ordinal), id, "a README rule-table row");
            Require(statusCoverage.Contains(id), id, "the README project status line");
            Require(verificationCoverage.Contains(id), id, "the README verification map's analyzer-test list");
            Require(package.Contains($"| {id} |", StringComparison.Ordinal), id, "a PACKAGE.md rule-table row");
            Require(Regex.IsMatch(releases, $@"^{id} \|", RegexOptions.Multiline), id, "an AnalyzerReleases entry");
            Require(changelog.Contains(id, StringComparison.Ordinal), id, "a CHANGELOG entry");
        }

        AssertNoProblems(problems);

        void Require(bool present, string id, string location)
        {
            if (!present)
            {
                problems.Add($"{id}: missing {location}");
            }
        }
    }

    [Fact]
    public void RuleArtifactsHaveNoOrphans()
    {
        var shipped = ShippedRuleIds().ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var page in Directory.GetFiles(Path.Combine(Root, "docs", "rules"), "*.md"))
        {
            var id = Path.GetFileNameWithoutExtension(page);
            if (!shipped.Contains(id))
            {
                problems.Add($"docs/rules/{id}.md: no analyzer ships {id}");
            }
        }

        foreach (var folder in SpecCapabilities().Where(static folder => Regex.IsMatch(folder, @"^efd\d{3}-")))
        {
            var id = folder[..6].ToUpperInvariant();
            if (!shipped.Contains(id))
            {
                problems.Add($"openspec/specs/{folder}: no analyzer ships {id}");
            }
        }

        AddOrphanRows(Read("README.md"), @"^\| \*\*(EFD\d{3})\*\* \|", "README rule table");
        AddOrphanRows(Read("src/EFDoctor.Cli/Package/PACKAGE.md"), @"^\| (EFD\d{3}) \|", "PACKAGE.md rule table");

        AssertNoProblems(problems);

        void AddOrphanRows(string text, string rowPattern, string location)
        {
            foreach (Match row in Regex.Matches(text, rowPattern, RegexOptions.Multiline))
            {
                if (!shipped.Contains(row.Groups[1].Value))
                {
                    problems.Add($"{location}: row for {row.Groups[1].Value}, which no analyzer ships");
                }
            }
        }
    }

    [Fact]
    public void SolutionHasOneFixturesFolderAndEveryFixtureProject()
    {
        var solution = Read("EFDoctor.sln").Replace('\\', '/');
        var problems = new List<string>();

        var fixturesFolders = Regex.Matches(solution, @"= ""Fixtures"", ""Fixtures"",").Count;
        if (fixturesFolders != 1)
        {
            problems.Add($"EFDoctor.sln: expected exactly one \"Fixtures\" solution folder but found {fixturesFolders}; `dotnet sln add` creates a duplicate (see CONTRIBUTING.md)");
        }

        foreach (var project in Directory.GetFiles(Path.Combine(Root, "tests", "Fixtures"), "*.csproj", SearchOption.AllDirectories))
        {
            // A fixture that marks itself as a test project stays out of the solution, because the
            // solution-wide `dotnet test` run would otherwise try to execute it as a test assembly.
            var relative = Path.GetRelativePath(Root, project).Replace('\\', '/');
            if (!relative.Contains("/bin/", StringComparison.Ordinal)
                && !relative.Contains("/obj/", StringComparison.Ordinal)
                && !File.ReadAllText(project).Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase)
                && !solution.Contains(relative, StringComparison.Ordinal))
            {
                problems.Add($"EFDoctor.sln: fixture project {relative} is not in the solution");
            }
        }

        AssertNoProblems(problems);
    }

    [Fact]
    public void SpecTraitsReferToExistingScenarios()
    {
        var scenarios = SpecScenarios();
        var problems = new List<string>();
        foreach (var trait in SpecTraits())
        {
            if (!scenarios.TryGetValue(trait.Capability, out var names))
            {
                problems.Add($"{trait.File}: Spec trait \"{trait.Value}\" names unknown capability \"{trait.Capability}\"");
            }
            else if (!names.Contains(trait.Scenario))
            {
                problems.Add($"{trait.File}: Spec trait \"{trait.Value}\" names no scenario in {trait.Capability}");
            }
        }

        AssertNoProblems(problems);
    }

    [Fact]
    public void TracedCapabilitiesCoverEveryScenario()
    {
        var scenarios = SpecScenarios();
        var traits = SpecTraits().ToList();
        var problems = new List<string>();
        foreach (var capability in traits.Select(static trait => trait.Capability).Distinct(StringComparer.Ordinal).Where(scenarios.ContainsKey))
        {
            var names = scenarios[capability];
            foreach (var duplicate in names.GroupBy(static name => name, StringComparer.Ordinal).Where(static group => group.Count() > 1))
            {
                problems.Add($"{capability}: scenario name \"{duplicate.Key}\" is not unique");
            }

            var covered = traits
                .Where(trait => trait.Capability == capability)
                .Select(static trait => trait.Scenario)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var name in names.Distinct(StringComparer.Ordinal).Where(name => !covered.Contains(name)))
            {
                problems.Add($"{capability}: scenario \"{name}\" has no test with [Trait(\"Spec\", \"{capability}/{name}\")]");
            }
        }

        AssertNoProblems(problems);
    }

    private static IReadOnlyList<string> ShippedRuleIds() =>
        typeof(SaveChangesInLoopAnalyzer).Assembly.GetTypes()
            .Where(static type => !type.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(type)
                && type.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .SelectMany(static type => ((DiagnosticAnalyzer)Activator.CreateInstance(type)!).SupportedDiagnostics)
            .Select(static descriptor => descriptor.Id)
            .Where(static id => RuleIdPattern.IsMatch(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();

    private static HashSet<string> RegisteredRuleIds()
    {
        var field = typeof(WorkspaceAnalyzer).GetField("Analyzers", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("WorkspaceAnalyzer.Analyzers was not found; update RepositoryConsistencyTests.");
        var analyzers = (ImmutableArray<DiagnosticAnalyzer>)field.GetValue(null)!;
        return analyzers
            .SelectMany(static analyzer => analyzer.SupportedDiagnostics)
            .Select(static descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Rule IDs named directly or inside an "EFDnnn through EFDmmm" range.
    private static HashSet<string> Coverage(string text)
    {
        var ids = RuleIdPattern.Matches(text).Select(static match => match.Value).ToHashSet(StringComparer.Ordinal);
        foreach (Match range in RuleRangePattern.Matches(text))
        {
            var first = int.Parse(range.Groups[1].Value);
            var last = int.Parse(range.Groups[2].Value);
            for (var number = first; number <= last; number++)
            {
                ids.Add($"EFD{number:D3}");
            }
        }

        return ids;
    }

    private static IReadOnlyList<string> SpecCapabilities() =>
        Directory.GetDirectories(Path.Combine(Root, "openspec", "specs"))
            .Select(static directory => Path.GetFileName(directory))
            .ToList();

    private static Dictionary<string, List<string>> SpecScenarios() =>
        SpecCapabilities().ToDictionary(
            static capability => capability,
            static capability => ScenarioPattern
                .Matches(Read(Path.Combine("openspec", "specs", capability, "spec.md")))
                .Select(static match => match.Groups[1].Value)
                .ToList(),
            StringComparer.Ordinal);

    private static IEnumerable<SpecTrait> SpecTraits()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.StartsWith("tests/Fixtures/", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in SpecTraitPattern.Matches(File.ReadAllText(file)))
            {
                var value = match.Groups[1].Value;
                var separator = value.IndexOf('/');
                yield return separator < 0
                    ? new SpecTrait(relative, value, value, string.Empty)
                    : new SpecTrait(relative, value, value[..separator], value[(separator + 1)..]);
            }
        }
    }

    private static string SingleLine(string text, string prefix) =>
        text.Split('\n').SingleOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"README line starting with \"{prefix}\" was not found; update RepositoryConsistencyTests.");

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    private static void AssertNoProblems(IReadOnlyCollection<string> problems)
    {
        if (problems.Count > 0)
        {
            Assert.Fail($"{problems.Count} repository consistency problem(s):\n" + string.Join("\n", problems));
        }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EFDoctor.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the EFDoctor repository root.");
    }

    private sealed record SpecTrait(string File, string Value, string Capability, string Scenario);
}
