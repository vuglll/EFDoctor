using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using EFDoctor.Analyzers;

namespace EFDoctor.Analyzers.Tests;

public sealed class MultipleEnumerationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var count = query.Count();
                var list = query.ToList();
            }
            """, 1);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                if (query.Any())
                {
                    foreach (var entity in query)
                    {
                    }
                }
            }
            """, 1);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities;
                var a = query.ToList();
                var b = query.ToList();
            }
            """, 1);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var counts = new List<int> { 1 }.Select(_ => query.Count()).ToList();
                var list = query.ToList();
            }
            """, 1);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var total = query.Sum(entity => entity.Total);
                var list = query.ToList();
            }
            """, 1);

        yield return Case("""
            static void Run(TestContext context, bool flag)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                if (!query.Any())
                {
                    return;
                }

                if (flag)
                {
                    var list = query.ToList();
                }
                else
                {
                    var count = query.Count();
                }
            }
            """, 1);
        yield return Case("""
            static void Run(TestContext context)
            {
                var all = context.Entities.AsNoTracking();
                var query = all.Where(entity => entity.Id > 0);
                var count = query.Count();
                var list = query.ToList();
            }
            """, 1);
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var list = query.ToList();
            }
            """);

        yield return Case("""
            static void Run(TestContext context)
            {
                var list = context.Entities.ToList();
                var count = list.Count;
                var filtered = list.Where(entity => entity.Id > 0).ToList();
            }
            """);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var a = query.Count();
                query = context.Entities.Where(entity => entity.Id < 0);
                var b = query.Count();
            }
            """);

        yield return Case("""
            static void Run(System.Linq.IQueryable<Entity> query)
            {
                var a = query.Count();
                var b = query.ToList();
            }
            """);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = new System.Collections.Generic.List<Entity>().Where(entity => entity.Id > 0);
                var a = query.Count();
                var b = query.ToList();
            }
            """);

        yield return Case("""
            static void Run(TestContext context)
            {
                var ids = context.Entities.Where(entity => entity.Id > 0).Select(entity => entity.Id);
                var first = context.Entities.Where(entity => ids.Contains(entity.Id)).ToList();
                var second = context.Entities.Where(entity => !ids.Contains(entity.Id)).ToList();
                var third = context.Entities.Where(entity => ids.Any()).ToList();
            }
            """);

        yield return Case("""
            static void Run(TestContext context)
            {
                var names = context.Entities.Where(entity => entity.Id > 0).Select(entity => entity.Name);
                var matches = context.Entities
                    .Where(entity => entity.Children.Any(child => names.Contains(child.Name))
                        && !entity.Children.Any(child => child.Id > 0 && names.Contains(child.Name)))
                    .ToList();
            }
            """);

        yield return Case("""
            static void Run(TestContext context)
            {
                var query = context.Set<Entity>();
                var hasA = query.Any(entity => entity.Name == "A");
                var b = query.FirstOrDefault(entity => entity.Name == "B");
                var c = query.Where(entity => entity.Name == "C").ToList();
            }
            """);

        yield return Case("""
            static async System.Threading.Tasks.Task Run(TestContext context, bool flag)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var map = flag ? await query.ToDictionaryAsync(entity => entity.Id) : query.ToDictionary(entity => entity.Id);
            }
            """);

        yield return Case("""
            static void Run(TestContext context, bool flag)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                if (flag)
                {
                    var list = query.ToList();
                }
                else
                {
                    foreach (var entity in query)
                    {
                    }
                }
            }
            """);
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd020-multiple-enumeration/Query executed twice")]
    [Trait("Spec", "efd020-multiple-enumeration/Foreach plus terminal")]
    [Trait("Spec", "efd020-multiple-enumeration/Use inside a delegate lambda is an execution")]
    [Trait("Spec", "efd020-multiple-enumeration/Selector terminals still count")]
    [Trait("Spec", "efd020-multiple-enumeration/Execution before a branch still counts")]
    [Trait("Spec", "efd020-multiple-enumeration/Analyzer fixture suite")]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        var matches = await AnalyzeAsync(source);
        Assert.Equal(expectedCount, matches.Count(static diagnostic => diagnostic.Id == MultipleEnumerationAnalyzer.DiagnosticId));
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd020-multiple-enumeration/Executed once is not reported")]
    [Trait("Spec", "efd020-multiple-enumeration/Materialized once and reused is not reported")]
    [Trait("Spec", "efd020-multiple-enumeration/Reassigned local is not reported")]
    [Trait("Spec", "efd020-multiple-enumeration/Arbitrary or in-memory source")]
    [Trait("Spec", "efd020-multiple-enumeration/Use inside an expression-tree lambda is not an execution")]
    [Trait("Spec", "efd020-multiple-enumeration/Predicate terminals are distinct queries")]
    [Trait("Spec", "efd020-multiple-enumeration/Mutually exclusive branches are not added together")]
    [Trait("Spec", "efd020-multiple-enumeration/Analyzer fixture suite")]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        var matches = await AnalyzeAsync(source);
        Assert.DoesNotContain(matches, static diagnostic => diagnostic.Id == MultipleEnumerationAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd020-multiple-enumeration/Complete structured finding")]
    [Trait("Spec", "efd020-multiple-enumeration/Exact diagnostic location")]
    [Trait("Spec", "efd020-multiple-enumeration/Qualified remediation")]
    public async Task FindingCarriesEvidenceConfidenceAndDocumentationKey()
    {
        var source = QuerySource("""
            static void Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Id > 0);
                var count = query.Count();
                var list = query.ToList();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("enumerated 2 times", diagnostic.Properties[DiagnosticPropertyNames.Evidence]!, StringComparison.Ordinal);
        Assert.Equal("EFD020", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Equal("query = context.Entities.Where(entity => entity.Id > 0)", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Contains("Materialize the query once", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!, StringComparison.Ordinal);
        Assert.Contains("suppress with a recorded reason", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation]!, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd020-multiple-enumeration/Pragma suppression")]
    public async Task PragmaSuppressionIsHonored()
    {
        var source = QuerySource("""
            static void Run(TestContext context)
            {
            #pragma warning disable EFD020
                var query = context.Entities.Where(entity => entity.Id > 0);
            #pragma warning restore EFD020
                var count = query.Count();
                var list = query.ToList();
            }
            """);

        Assert.DoesNotContain(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == MultipleEnumerationAnalyzer.DiagnosticId);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source.Contains("class TestContext", StringComparison.Ordinal) ? source : QuerySource(source),
            analyzer: new MultipleEnumerationAnalyzer(),
            diagnosticId: MultipleEnumerationAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount) => [QuerySource(member), expectedCount];

    private static object[] Case(string member) => [QuerySource(member)];

    private static string QuerySource(string member) => $$"""
        using System.Collections.Generic;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        sealed class Entity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public decimal Total { get; set; }
            public List<Entity> Children { get; set; } = new();
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
