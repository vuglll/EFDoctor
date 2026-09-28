using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class LeadingWildcardSearchAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context, string domain) => context.Customers.Where(customer => customer.Email.EndsWith(domain));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.ToLower().Contains(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.ToUpper().EndsWith(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Orders.Where(order => order.Customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term) || customer.Email.Contains(term));", 2);
        yield return Case("static object Run(TestContext context) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, \"%smith\"));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, \"%\" + term + \"%\"));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, $\"%{term}\"));");
        yield return Case("static object Run(TestContext context) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, \"_mith\"));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, \"%\" + term, \"\\\\\"));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name.ToLower(), \"%\" + term));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.AnyAsync(customer => customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Count(customer => customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.FirstOrDefault(customer => customer.Email.EndsWith(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Set<Customer>().Where(customer => customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context, string term) => Queryable.Where(context.Customers, customer => customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Include(customer => customer.Tags).AsNoTracking().TagWith(\"search\").OrderBy(customer => customer.Id).Where(customer => customer.Name.Contains(term));");
        yield return Case("static object Run(TestContext context) => context.Customers.Where(customer => customer.Name.Contains(\"smith\"));");
        yield return Case("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, Wildcard + term));\nconst string Wildcard = \"%\";");
        yield return Case("static object Run(TestContext context, string term) { IQueryable<Customer> query = context.Customers.AsNoTracking(); return query.Where(customer => customer.Name.Contains(term)); }");
        yield return Case("static object Run(TestContext context, string value) { var customers = context.Customers; var active = customers.Where(customer => customer.Id > 0); return active.Where(customer => customer.Name.Contains(value)); }");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static object Run(TestContext context, string prefix) => context.Customers.Where(customer => customer.Name.StartsWith(prefix));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, term + \"%\"));");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, \"smith%\"));");
        yield return SourceCase("static object Run(TestContext context, string pattern) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, pattern));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, $\"{term}%\"));");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Where(customer => EF.Functions.Like(customer.Name, \"\"));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => EF.Functions.Like(term, \"%smith\"));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase));");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Where(customer => customer.Name.Contains('x'));");
        yield return SourceCase("static object Run(TestContext context, List<string> names) => context.Customers.Where(customer => names.Contains(customer.Name));");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Where(customer => customer.Name.Contains(customer.Nickname));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Tags.Any(tag => tag.Name.Contains(term)));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.DisplayName.Contains(term));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Select(customer => new CustomerView { Name = customer.Name }).Where(view => view.Name.Contains(term));");
        yield return SourceCase("static object Run(IQueryable<Customer> query, string term) => query.Where(customer => customer.Name.Contains(term));");
        yield return SourceCase("static object Run(TestContext context, string term, bool untracked) { IQueryable<Customer> query = context.Customers; if (untracked) { query = query.AsNoTracking(); } return query.Where(customer => customer.Name.Contains(term)); }");
        yield return SourceCase("static object Run(string term) => new[] { new Customer() }.AsQueryable().Where(customer => customer.Name.Contains(term));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.AsEnumerable().Where(customer => customer.Name.Contains(term));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Code.Contains(term));");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Where(customer => Matcher.Like(customer.Name, \"%smith\"));", "static class Matcher { public static bool Like(string value, string pattern) => false; }");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Select(customer => customer.Name.Contains(term));");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.ToLower() == term);");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Missing.Contains(term));");
        yield return SourceCase("""
            static object Run(TestContext context, string term)
            {
            #if NEVER
                return context.Customers.Where(customer => customer.Name.Contains(term));
            #else
                return context.Customers;
            #endif
            }
            """);
    }

    [Theory]
    [Trait("Spec", "efd023-leading-wildcard-search/Substring search")]
    [Trait("Spec", "efd023-leading-wildcard-search/Suffix search")]
    [Trait("Spec", "efd023-leading-wildcard-search/Case-transformed column")]
    [Trait("Spec", "efd023-leading-wildcard-search/Reference navigation path")]
    [Trait("Spec", "efd023-leading-wildcard-search/Several searches in one predicate")]
    [Trait("Spec", "efd023-leading-wildcard-search/Constant leading wildcard")]
    [Trait("Spec", "efd023-leading-wildcard-search/Concatenated leading wildcard")]
    [Trait("Spec", "efd023-leading-wildcard-search/Interpolated leading wildcard")]
    [Trait("Spec", "efd023-leading-wildcard-search/Terminal predicate overload")]
    [Trait("Spec", "efd023-leading-wildcard-search/Predicate after supported composition")]
    [Trait("Spec", "efd023-leading-wildcard-search/Query stored in a followable local")]
    [Trait("Spec", "efd023-leading-wildcard-search/Curated validation suite")]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsLeadingWildcardSearches(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Equal(LeadingWildcardSearchAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD023", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Equal(LeadingWildcardSearchAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
            Assert.Contains("use StartsWith", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
            Assert.Matches(@"(Contains|EndsWith|Like)\(.*\)$", Text(diagnostic));
        });
    }

    [Theory]
    [Trait("Spec", "efd023-leading-wildcard-search/Prefix pattern")]
    [Trait("Spec", "efd023-leading-wildcard-search/Pattern of unknown shape")]
    [Trait("Spec", "efd023-leading-wildcard-search/Prefix search")]
    [Trait("Spec", "efd023-leading-wildcard-search/StringComparison overload")]
    [Trait("Spec", "efd023-leading-wildcard-search/Collection membership")]
    [Trait("Spec", "efd023-leading-wildcard-search/Column-to-column search")]
    [Trait("Spec", "efd023-leading-wildcard-search/Nested lambda")]
    [Trait("Spec", "efd023-leading-wildcard-search/Computed property")]
    [Trait("Spec", "efd023-leading-wildcard-search/Unproven or in-memory source")]
    [Trait("Spec", "efd023-leading-wildcard-search/Unrelated same-named methods")]
    [Trait("Spec", "efd023-leading-wildcard-search/Curated validation suite")]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportPrefixExcludedOrUnrelatedShapes(string source)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Finding contract")]
    [Trait("Spec", "efd023-leading-wildcard-search/Reference navigation path")]
    public async Task ReportsOnSearchCallWithCompleteEvidence()
    {
        var source = QuerySource("static object Run(TestContext context, string term) => context.Orders.Where(order => order.Id > 0).Where(order => order.Customer.Name.Contains(term));");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("order.Customer.Name.Contains(term)", Text(diagnostic));
        Assert.Equal(source.IndexOf("order.Customer.Name.Contains(term)", StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
        Assert.Equal(
            "The query is traced to DbSet origin 'context.Orders'. The predicate of Queryable.Where searches column 'Order.Customer.Name' with Contains: Contains matches anywhere in the value, which translates to a pattern with a leading wildcard, so the database cannot seek an ordinary index on the column.",
            diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Case-transformed column")]
    [Trait("Spec", "efd023-leading-wildcard-search/Suffix search")]
    public async Task EvidenceNamesCaseTransformationAndSuffixReason()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Email.ToUpper().EndsWith(term));")));

        Assert.Equal("customer.Email.ToUpper().EndsWith(term)", Text(diagnostic));
        Assert.Contains("searches column 'Customer.Email' after ToUpper() with EndsWith: EndsWith matches a suffix", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd023-leading-wildcard-search/Concatenated leading wildcard")]
    [Trait("Spec", "efd023-leading-wildcard-search/Interpolated leading wildcard")]
    [InlineData("EF.Functions.Like(customer.Name, \"%\" + term)", "'%'")]
    [InlineData("EF.Functions.Like(customer.Name, $\"_{term}\")", "'_'")]
    public async Task EvidenceNamesLeadingWildcardOfLikePattern(string predicate, string wildcard)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource($"static object Run(TestContext context, string term) => context.Customers.Where(customer => {predicate});")));

        Assert.Equal(predicate, Text(diagnostic));
        Assert.Contains($"with EF.Functions.Like: the LIKE pattern starts with {wildcard}", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Static extension syntax")]
    public async Task StaticAndReducedSyntaxProduceEquivalentFindings()
    {
        var reduced = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));")));
        var @static = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context, string term) => Queryable.Where(context.Customers, customer => customer.Name.Contains(term));")));

        Assert.Equal(reduced.Properties[DiagnosticPropertyNames.Evidence], @static.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal("customer.Name.Contains(term)", Text(@static));
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/SQL Server provider referenced")]
    public async Task RemediationLeadsWithSqlServerFullTextSearch()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(SimpleMember), includeSqlServer: true));

        Assert.StartsWith("For word or phrase search on SQL Server, use a full-text index with EF.Functions.Contains or EF.Functions.FreeText.", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.EndsWith("Referenced EF Core provider: SQL Server.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/PostgreSQL provider referenced")]
    public async Task RemediationLeadsWithPostgreSqlTrigramIndex()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(SimpleMember), includeNpgsql: true));

        Assert.StartsWith("On PostgreSQL, a pg_trgm GIN trigram index", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.Contains("tsvector", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.EndsWith("Referenced EF Core provider: PostgreSQL (Npgsql).", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemediationGivesBothOptionsWithoutProvider()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(SimpleMember)));

        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation];
        Assert.StartsWith("Consider full-text search", remediation, StringComparison.Ordinal);
        Assert.Contains("pg_trgm", remediation, StringComparison.Ordinal);
        Assert.Contains("reversed or computed column", remediation, StringComparison.Ordinal);
        Assert.Contains("suppress EFD023 with a recorded reason", remediation, StringComparison.Ordinal);
        Assert.DoesNotContain("Referenced EF Core provider", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Case-transformed column")]
    public async Task CaseTransformedSearchIsNotAlsoReportedByEfd009()
    {
        var source = QuerySource("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.ToLower().Contains(term));");

        Assert.Single(await AnalyzeAsync(source));
        Assert.Empty(await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            analyzer: new NonSargableCaseTransformAnalyzer(),
            diagnosticId: NonSargableCaseTransformAnalyzer.DiagnosticId));
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Suppressed finding")]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD023 // Reviewed: the customer table is small and rarely searched.
            static object Suppressed(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));
            #pragma warning restore EFD023
            static object Reported(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Suppressed finding")]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            QuerySource(SimpleMember),
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new LeadingWildcardSearchAnalyzer(),
            diagnosticId: LeadingWildcardSearchAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Suppressed finding")]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = QuerySource("""
            [SuppressMessage("Performance", "EFD023", Justification = "The customer table is small and rarely searched.")]
            static object Suppressed(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));
            static object Reported(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Generated source")]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource(SimpleMember)));
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Unproven or in-memory source")]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var source = """
            using System.Linq;
            static class Subject
            {
                static object Run(IQueryable<string> query, string term) => query.Where(value => value.Contains(term));
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new LeadingWildcardSearchAnalyzer(),
            diagnosticId: LeadingWildcardSearchAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd023-leading-wildcard-search/Curated validation suite")]
    public void CuratedSourcesCompileExceptDeliberatelyMalformedCase()
    {
        var failures = CuratedSources()
            .Where(static source => !source.Contains("customer.Missing", StringComparison.Ordinal))
            .SelectMany(static source => AnalyzerTestHarness.GetCompilationDiagnostics(source, includeRelational: true)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => $"{diagnostic.Id} {diagnostic.GetMessage()}"))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void DescriptorUsesPerformanceInfoMetadata()
    {
        Assert.Equal("EFD023", LeadingWildcardSearchAnalyzer.Rule.Id);
        Assert.Equal("Performance", LeadingWildcardSearchAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Info, LeadingWildcardSearchAnalyzer.Rule.DefaultSeverity);
        Assert.True(LeadingWildcardSearchAnalyzer.Rule.IsEnabledByDefault);
    }

    private const string SimpleMember = "static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Name.Contains(term));";

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        bool includeSqlServer = false,
        bool includeNpgsql = false) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            includeSqlServer: includeSqlServer,
            includeNpgsql: includeNpgsql,
            analyzer: new LeadingWildcardSearchAnalyzer(),
            diagnosticId: LeadingWildcardSearchAnalyzer.DiagnosticId);

    internal static IEnumerable<string> CuratedSources() =>
        PositiveCases().Concat(NegativeCases()).Select(static row => (string)row[0]);

    private static object[] Case(string member, int expectedCount = 1) => [QuerySource(member), expectedCount];

    private static object[] SourceCase(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        sealed class Tag
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        sealed class ProductCode
        {
            public string Value { get; set; } = "";
            public bool Contains(string text) => Value.Contains(text);
        }

        sealed class Customer
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Nickname { get; set; } = "";
            public string Email { get; set; } = "";
            public string DisplayName => Name + " (customer)";
            public ProductCode Code { get; set; } = new();
            public List<Tag> Tags { get; set; } = new();
        }

        sealed class Order
        {
            public int Id { get; set; }
            public Customer Customer { get; set; } = new();
        }

        sealed class CustomerView
        {
            public string Name { get; set; } = "";
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Customer> Customers => Set<Customer>();
            public DbSet<Order> Orders => Set<Order>();
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;
}
