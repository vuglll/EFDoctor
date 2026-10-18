using System.Collections.Immutable;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class ClientMethodInQueryAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context) => context.Orders.Where(o => Rules.IsOverdue(o)).ToList();", "Rules.IsOverdue(o)");
        yield return Case("static object Run(TestContext context) => Queryable.Where(context.Orders, o => Rules.IsOverdue(o)).ToList();", "Rules.IsOverdue(o)");
        yield return Case("static object Run(TestContext context) => context.Orders.Where(o => !o.IsClosed()).ToList();", "o.IsClosed()");
        yield return Case("static object Run(TestContext context, string value) => context.Orders.Where(o => o.Name.Clean() == value).ToList();", "o.Name.Clean()");
        yield return Case("static object Run(TestContext context) => context.Orders.Where(o => o.Id > 0 && Rules.Weight(o.Status) > 2).ToList();", "Rules.Weight(o.Status)");
        yield return Case("static object Run(TestContext context) => context.Orders.OrderBy(o => Rules.Weight(o.Status)).ToList();", "Rules.Weight(o.Status)");
        yield return Case("static object Run(TestContext context) => context.Orders.OrderBy(o => o.Id).ThenByDescending(o => Rules.Weight(o.Status)).ToList();", "Rules.Weight(o.Status)");
        yield return Case("static bool Run(TestContext context) => context.Orders.Any(o => Rules.IsOverdue(o));", "Rules.IsOverdue(o)");
        yield return Case("static object? Run(TestContext context) => context.Orders.FirstOrDefault(o => Rules.IsOverdue(o));", "Rules.IsOverdue(o)");
        yield return Case("static Task<int> Run(TestContext context) => context.Orders.CountAsync(o => Rules.IsOverdue(o));", "Rules.IsOverdue(o)");
        yield return Case("static object Run(TestContext context) => context.Orders.Where(o => o.Lines.Any(l => Rules.IsDiscounted(l))).ToList();", "Rules.IsDiscounted(l)");
        yield return Case("static object Run(TestContext context) { var query = context.Orders.AsQueryable(); if (context != null) { query = query.Where(o => Rules.IsOverdue(o)); } return query.ToList(); }", "Rules.IsOverdue(o)");
        yield return Case("static object Run(TestContext context) { bool Open(Order o) => o.Status < 3; return context.Orders.Where(o => Open(o)).ToList(); }", "Open(o)");
        yield return Case("static object Run(TestContext context) => context.Orders.AsNoTracking().Where(o => o.Id > 0).Where(o => Rules.IsOverdue(o)).ToList();", "Rules.IsOverdue(o)");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return Negative("static object Run(TestContext context, DateTime now) => context.Orders.Where(o => o.DueUtc < Rules.Cutoff(now)).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => o.Status == Rules.Weight(2)).ToList();");
        yield return Negative("static object Run(List<Order> orders) => orders.Where(o => Rules.IsOverdue(o)).ToList();");
        yield return Negative("static object Run(List<Order> orders) => orders.AsQueryable().Where(o => Rules.IsOverdue(o)).ToList();");
        yield return Negative("static object Run(IQueryable<Order> orders) => orders.Where(o => Rules.IsOverdue(o)).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.AsEnumerable().Where(o => Rules.IsOverdue(o)).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.ToList().Where(o => Rules.IsOverdue(o)).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Select(o => new { o.Id, Overdue = Rules.IsOverdue(o) }).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => string.IsNullOrEmpty(o.Name) || o.Name.StartsWith(\"A\")).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => EF.Functions.Like(o.Name, \"%a\")).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Functions.Mapped(o.Status) > 1).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Functions.Registered(o.Status) > 1).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => TestContext.Distance(o.Status) > 1).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Functions.Stub(o.Status) > 1).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Functions.BlockStub(o.Status) > 1).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => o.IsProjected()).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Rules.Injected(o)).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Rules.Computed(o)).ToList();");
        yield return Negative("static object Run(TestContext context) => context.Orders.Where(o => Rules.Expanded(o)).ToList();");
        yield return Negative("static object Run(TestContext context) { Func<Order, bool> predicate = Rules.IsOverdue; return context.Orders.Where(o => o.Id > 0).ToList().Where(predicate).ToList(); }");
        yield return Negative("static object Run(TestContext context) => context.Orders.GroupBy(o => o.Status).Select(g => g.Count()).ToList();");
        yield return Negative("static bool Run(Order order) => Rules.IsOverdue(order);");
    }

    [Theory]
    [Trait("Spec", "efd028-client-method-in-query/Static helper in a Where predicate")]
    [Trait("Spec", "efd028-client-method-in-query/Instance method on the entity")]
    [Trait("Spec", "efd028-client-method-in-query/Extension method over a column")]
    [Trait("Spec", "efd028-client-method-in-query/Ordering key")]
    [Trait("Spec", "efd028-client-method-in-query/Predicate terminal and async terminal")]
    [Trait("Spec", "efd028-client-method-in-query/Call nested in an inner lambda")]
    [Trait("Spec", "efd028-client-method-in-query/Query through a conditionally composed local")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsClientMethodsInTranslatedQueries(string source, string expectedText)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal(expectedText, Text(diagnostic));
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [Trait("Spec", "efd028-client-method-in-query/Call that reads no lambda parameter")]
    [Trait("Spec", "efd028-client-method-in-query/In-memory or unproven source")]
    [Trait("Spec", "efd028-client-method-in-query/Projection is not reported")]
    [Trait("Spec", "efd028-client-method-in-query/Framework and EF methods are not reported")]
    [Trait("Spec", "efd028-client-method-in-query/DbFunction attribute")]
    [Trait("Spec", "efd028-client-method-in-query/HasDbFunction registration")]
    [Trait("Spec", "efd028-client-method-in-query/Database-function stub")]
    [Trait("Spec", "efd028-client-method-in-query/Query-rewriting attribute")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportTranslatableOrClientSideShapes(string source)
    {
        Assert.DoesNotContain(AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    [Trait("Spec", "efd028-client-method-in-query/Complete structured finding")]
    public async Task FindingCarriesTheCompleteContract()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Orders.Where(o => Rules.IsOverdue(o)).ToList();")));

        Assert.Equal(ClientMethodInQueryAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
        Assert.Equal("Correctness", diagnostic.Descriptor.Category);
        Assert.Equal("Method 'IsOverdue' cannot be translated to SQL inside this query's Where", diagnostic.GetMessage());
        Assert.Equal("high", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Equal("EFD028", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        Assert.Contains("Rules.IsOverdue(Order)", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Contains("DbSet origin 'context.Orders'", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
        Assert.Equal(ClientMethodInQueryAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
        Assert.Contains("database function", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("suppress", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsEachClientMethodOnce()
    {
        var diagnostics = await AnalyzeAsync(QuerySource("static object Run(TestContext context) => context.Orders.Where(o => Rules.IsOverdue(o) || o.IsClosed()).OrderBy(o => Rules.Weight(o.Status)).ToList();"));

        Assert.Equal(["Rules.IsOverdue(o)", "o.IsClosed()", "Rules.Weight(o.Status)"], diagnostics.OrderBy(static d => d.Location.SourceSpan.Start).Select(Text));
    }

    [Fact]
    [Trait("Spec", "efd028-client-method-in-query/Pragma and editor configuration suppression")]
    public async Task HonorsPragmaSuppressMessageAndEditorConfig()
    {
        var source = QuerySource("""
            static object Pragma(TestContext context)
            {
            #pragma warning disable EFD028
                return context.Orders.Where(o => Rules.IsOverdue(o)).ToList();
            #pragma warning restore EFD028
            }

            [SuppressMessage("Correctness", "EFD028", Justification = "Translated by a plugin.")]
            static object Attributed(TestContext context) => context.Orders.Where(o => Rules.IsOverdue(o)).ToList();

            static object Reported(TestContext context) => context.Orders.Where(o => o.IsClosed()).ToList();
            """);

        Assert.Equal("o.IsClosed()", Text(Assert.Single(await AnalyzeAsync(source))));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new ClientMethodInQueryAnalyzer(),
            diagnosticId: ClientMethodInQueryAnalyzer.DiagnosticId));
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource("static object Run(TestContext context) => context.Orders.Where(o => Rules.IsOverdue(o)).ToList();")));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new ClientMethodInQueryAnalyzer(),
            diagnosticId: ClientMethodInQueryAnalyzer.DiagnosticId);

    private static object[] Case(string member, string expectedText) => [QuerySource(member), expectedText];

    private static object[] Negative(string member) => [QuerySource(member)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string QuerySource(string member) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class ProjectableAttribute : Attribute { }
        sealed class InjectLambdaAttribute : Attribute { }
        sealed class ComputedAttribute : Attribute { }
        sealed class ExpandableAttribute : Attribute { }

        sealed class Line
        {
            public int Id { get; set; }
            public decimal Discount { get; set; }
        }

        sealed class Order
        {
            public int Id { get; set; }
            public int Status { get; set; }
            public string Name { get; set; } = "";
            public DateTime DueUtc { get; set; }
            public List<Line> Lines { get; set; } = new();

            public bool IsClosed() => Status == 9;

            [Projectable]
            public bool IsProjected() => Status == 1;
        }

        static class Rules
        {
            public static bool IsOverdue(Order order) => order.DueUtc < DateTime.UtcNow;
            public static bool IsDiscounted(Line line) => line.Discount > 0;
            public static int Weight(int status) => status * 2;
            public static DateTime Cutoff(DateTime now) => now.AddDays(-7);
            public static string Clean(this string value) => value.Trim();

            [InjectLambda]
            public static bool Injected(Order order) => order.Status > 0;

            [Computed]
            public static bool Computed(Order order) => order.Status > 0;

            [Expandable]
            public static bool Expanded(Order order) => order.Status > 0;
        }

        static class Functions
        {
            [DbFunction("mapped", "dbo")]
            public static int Mapped(int status) => status;

            public static int Registered(int status) => status + 1;

            public static int Stub(int status) => throw new NotSupportedException();

            public static int BlockStub(int status)
            {
                throw new InvalidOperationException("Database only.");
            }
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Order> Orders => Set<Order>();

            public static int Distance(int status) => status;

            protected override void OnModelCreating(ModelBuilder modelBuilder) =>
                modelBuilder.HasDbFunction(typeof(Functions).GetMethod(nameof(Functions.Registered))!);
        }

        static class Subject
        {
            {{member}}
        }
        """;
}
