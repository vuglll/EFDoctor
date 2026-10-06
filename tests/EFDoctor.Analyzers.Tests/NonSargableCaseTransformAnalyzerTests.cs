using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class NonSargableCaseTransformAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => email == customer.Email.ToUpper());");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() != email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => string.Equals(customer.Email.ToLower(), email));");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower().Equals(email));");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => email.Equals(customer.Email.ToLower()));");
        yield return Case("static object Run(TestContext context, string prefix) => context.Customers.Where(customer => customer.Email.ToLower().StartsWith(prefix));");
        yield return Case("static object Run(TestContext context, string email) => context.Orders.Where(order => order.Customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email || customer.AlternateEmail.ToUpper() == email);", 2);
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Id > 0 && customer.Email.ToUpper() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Any(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Count(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.FirstOrDefault(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.FirstOrDefaultAsync(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.AnyAsync(customer => customer.Email.ToUpper() == email, CancellationToken.None);");
        yield return Case("static object Run(TestContext context, string email) => context.Set<Customer>().Where(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => Queryable.Where(context.Customers, customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Include(customer => customer.Tags).AsNoTracking().TagWith(\"lookup\").Where(customer => customer.Id > 0).OrderBy(customer => customer.Id).Where(customer => customer.Email.ToLower() == email);");
        yield return Case("static object Run(TestContext context) => context.Customers.Where(customer => customer.Email.ToLower() == \"ada@example.com\");");
        yield return Case("static object Run(TestContext context, string email) => context.Customers.Where(customer => (customer.Email.ToLower()) == email);");
        yield return Case("static object Run(TestContext context) => context.Customers.Where(customer => customer.Email.ToLower() == Target);\nstatic string Target = \"ada@example.com\";");
        yield return Case("static object Run(TestContext context, string email) { IQueryable<Customer> query = context.Customers.AsNoTracking(); return query.Where(customer => customer.Email.ToLower() == email); }");
        yield return Case("static object Run(TestContext context, string value) { var customers = context.Customers; var active = customers.Where(customer => customer.Id > 0); return active.Where(customer => customer.Email.ToLower() == value); }");
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email == email.ToLower());");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Where(customer => customer.Email.ToLower() == customer.AlternateEmail.ToLower());");
        yield return SourceCase("static object Run(TestContext context, string term) => context.Customers.Where(customer => customer.Email.ToLower().Contains(term));");
        yield return SourceCase("static object Run(TestContext context, string suffix) => context.Customers.Where(customer => customer.Email.ToLower().EndsWith(suffix));");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLowerInvariant() == email);");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToUpperInvariant() == email);");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower(CultureInfo.InvariantCulture) == email);");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.DisplayName.ToLower() == email);");
        yield return SourceCase("static object Run(TestContext context, string name) => context.Customers.Where(customer => customer.Tags.Any(tag => tag.Name.ToLower() == name));");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Select(customer => new CustomerView { Email = customer.Email }).Where(view => view.Email.ToLower() == email);");
        yield return SourceCase("static object Run(IQueryable<Customer> query, string email) => query.Where(customer => customer.Email.ToLower() == email);");
        yield return SourceCase("static object Run(TestContext context, string email, bool untracked) { IQueryable<Customer> query = context.Customers; if (untracked) { query = query.AsNoTracking(); } return query.Where(customer => customer.Email.ToLower() == email); }");
        yield return SourceCase("static object Run(string email) => new[] { new Customer() }.AsQueryable().Where(customer => customer.Email.ToLower() == email);");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.AsEnumerable().Where(customer => customer.Email.ToLower() == email);");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.Select(customer => customer.Email.ToLower());");
        yield return SourceCase("static object Run(TestContext context) => context.Customers.OrderBy(customer => customer.Email.ToLower());");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower().Equals(email, StringComparison.Ordinal));");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => string.Compare(customer.Email.ToLower(), email) == 0);");
        yield return SourceCase("static object Run(TestContext context, string code) => context.Customers.Where(customer => customer.Code.ToLower().Value == code);");
        yield return SourceCase("static object Run(Store store, string email) => store.Where(customer => customer.Email.ToLower() == email);", "sealed class Store { public Store Where(Func<Customer, bool> predicate) => this; }");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where((customer, index) => customer.Email.ToLower() == email);");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => Matches(customer.Email.ToLower(), email));\nstatic bool Matches(string left, string right) => left == right;");
        yield return SourceCase("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Missing.ToLower() == email);");
        yield return SourceCase("""
            static object Run(TestContext context, string email)
            {
            #if NEVER
                return context.Customers.Where(customer => customer.Email.ToLower() == email);
            #else
                return context.Customers;
            #endif
            }
            """);
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    public async Task ReportsCaseTransformedColumnComparisons(string source, int expectedCount)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(expectedCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Equal(NonSargableCaseTransformAnalyzer.RuleTitle, diagnostic.Descriptor.Title.ToString());
            Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Equal("EFD009", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
            Assert.Equal(NonSargableCaseTransformAnalyzer.Impact, diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact]);
            Assert.Contains("Normalize the comparison value in C#", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
            Assert.Matches(@"\.To(Lower|Upper)\(\)$", Text(diagnostic));
        });
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    public async Task DoesNotReportSargableExcludedOrUnrelatedShapes(string source)
    {
        var diagnostics = await AnalyzeAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ReportsOnTransformationCallWithCompleteEvidence()
    {
        var source = QuerySource("static object Run(TestContext context, string email) => context.Orders.Where(order => order.Id > 0).Where(order => order.Customer.Email.ToLower() == email);");

        var diagnostic = Assert.Single(await AnalyzeAsync(source));

        Assert.Equal("order.Customer.Email.ToLower()", Text(diagnostic));
        Assert.Equal(source.IndexOf("order.Customer.Email.ToLower()", StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
        Assert.Equal(
            "The query is traced to DbSet origin 'context.Orders'. The predicate of Queryable.Where applies ToLower() to column 'Order.Customer.Email' and compares the result using ==, so the database evaluates the function for each candidate row instead of seeking an index on the column.",
            diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Theory]
    [InlineData("customer.Email.ToLower() == email", "==")]
    [InlineData("email != customer.Email.ToLower()", "!=")]
    [InlineData("string.Equals(customer.Email.ToLower(), email)", "string.Equals")]
    [InlineData("customer.Email.ToLower().Equals(email)", "Equals")]
    [InlineData("email.Equals(customer.Email.ToLower())", "Equals")]
    [InlineData("customer.Email.ToLower().StartsWith(email)", "StartsWith")]
    public async Task EvidenceNamesComparisonKind(string predicate, string comparison)
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource($"static object Run(TestContext context, string email) => context.Customers.Where(customer => {predicate});")));

        Assert.Contains($"compares the result using {comparison},", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceNamesEfAsyncOperator()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context, string email) => context.Customers.SingleOrDefaultAsync(customer => customer.Email.ToUpper() == email);")));

        Assert.Contains("The predicate of EntityFrameworkQueryableExtensions.SingleOrDefaultAsync applies ToUpper()", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticAndReducedSyntaxProduceEquivalentFindings()
    {
        var reduced = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);")));
        var @static = Assert.Single(await AnalyzeAsync(QuerySource("static object Run(TestContext context, string email) => Queryable.Where(context.Customers, customer => customer.Email.ToLower() == email);")));

        Assert.Equal(reduced.Properties[DiagnosticPropertyNames.Evidence], @static.Properties[DiagnosticPropertyNames.Evidence]);
        Assert.Equal("customer.Email.ToLower()", Text(@static));
    }

    [Fact]
    public async Task RemediationLeadsWithSqlServerCollationAdvice()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(SimpleMember), includeSqlServer: true));

        Assert.StartsWith("SQL Server's default collations are case-insensitive, so remove the transformation", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
        Assert.EndsWith("Referenced EF Core provider: SQL Server.", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemediationLeadsWithPostgreSqlAdvice()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(SimpleMember), includeNpgsql: true));

        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation];
        Assert.StartsWith("PostgreSQL compares text case-sensitively by default", remediation, StringComparison.Ordinal);
        Assert.Contains("citext", remediation, StringComparison.Ordinal);
        Assert.Contains("EF.Functions.ILike", remediation, StringComparison.Ordinal);
        Assert.Contains("expression index on lower(column)", remediation, StringComparison.Ordinal);
        Assert.EndsWith("Referenced EF Core provider: PostgreSQL (Npgsql).", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemediationGivesBothOptionsWithoutProvider()
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(SimpleMember)));

        var remediation = diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation];
        Assert.StartsWith("If the column's collation is case-insensitive (the SQL Server default), remove the transformation", remediation, StringComparison.Ordinal);
        Assert.Contains("an index on the transformed expression", remediation, StringComparison.Ordinal);
        Assert.Contains("suppress EFD009 with a recorded reason", remediation, StringComparison.Ordinal);
        Assert.DoesNotContain("Referenced EF Core provider", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsPragmaSuppressionWhileSiblingStillReports()
    {
        var source = QuerySource("""
            #pragma warning disable EFD009 // Reviewed: an expression index on LOWER(Email) exists.
            static object Suppressed(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);
            #pragma warning restore EFD009
            static object Reported(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HonorsEditorConfigSuppression()
    {
        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            QuerySource(SimpleMember),
            includeRelational: true,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new NonSargableCaseTransformAnalyzer(),
            diagnosticId: NonSargableCaseTransformAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task HonorsSuppressMessageAttributeWhileSiblingStillReports()
    {
        var source = QuerySource("""
            [SuppressMessage("Performance", "EFD009", Justification = "An expression index on LOWER(Email) exists.")]
            static object Suppressed(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);
            static object Reported(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source));
        Assert.Contains("Reported", diagnostic.Location.SourceTree!.GetText().Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkipsGeneratedCode()
    {
        Assert.Empty(await AnalyzeAsync("// <auto-generated/>\n" + QuerySource(SimpleMember)));
    }

    [Fact]
    public async Task DoesNotReportWithoutEntityFrameworkReference()
    {
        var source = """
            using System.Linq;
            static class Subject
            {
                static object Run(IQueryable<string> query, string email) => query.Where(value => value.ToLower() == email);
            }
            """;

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeEntityFramework: false,
            analyzer: new NonSargableCaseTransformAnalyzer(),
            diagnosticId: NonSargableCaseTransformAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DescriptorUsesPerformanceInfoMetadata()
    {
        Assert.Equal("EFD009", NonSargableCaseTransformAnalyzer.Rule.Id);
        Assert.Equal("Performance", NonSargableCaseTransformAnalyzer.Rule.Category);
        Assert.Equal(DiagnosticSeverity.Info, NonSargableCaseTransformAnalyzer.Rule.DefaultSeverity);
        Assert.True(NonSargableCaseTransformAnalyzer.Rule.IsEnabledByDefault);
    }

    private const string SimpleMember = "static object Run(TestContext context, string email) => context.Customers.Where(customer => customer.Email.ToLower() == email);";

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        bool includeSqlServer = false,
        bool includeNpgsql = false) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            includeRelational: true,
            includeSqlServer: includeSqlServer,
            includeNpgsql: includeNpgsql,
            analyzer: new NonSargableCaseTransformAnalyzer(),
            diagnosticId: NonSargableCaseTransformAnalyzer.DiagnosticId);

    private static object[] Case(string member, int expectedCount = 1) => [QuerySource(member), expectedCount];

    private static object[] SourceCase(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string QuerySource(string member, string additionalTypes = "") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Globalization;
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
            public ProductCode ToLower() => this;
        }

        sealed class Customer
        {
            public int Id { get; set; }
            public string Email { get; set; } = "";
            public string AlternateEmail { get; set; } = "";
            public string DisplayName => Email + " (customer)";
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
            public string Email { get; set; } = "";
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
