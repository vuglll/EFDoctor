using System.Collections.Immutable;
using System.Text.RegularExpressions;
using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace EFDoctor.Analyzers.Tests;

// The test file posted with the rule proposal for existence and count checks
// (https://github.com/vuglll/EFDoctor/issues/13), kept as it was written. Every method in it
// must get exactly one finding, from EFD002 or EFD019, with the right recommendation.
public sealed class ExistenceProposalAcceptanceTests
{
    private const string Source = """
        #pragma warning disable EFD005

        using Microsoft.EntityFrameworkCore;
        using System;
        using System.Linq;

        class HasProducts
        {
            public bool M0(ProductContext db)
            {
                var products = db.Products.FirstOrDefault();
                return products is not null;
            }

            public bool M1(ProductContext db)
            {
                var products = db.Products.ToList();
                return products.Count > 0;
            }

            public bool M2(ProductContext db)
            {
                var products = db.Products.Count();
                return products > 0;
            }

            public bool M3(ProductContext db)
            {
                var products = db.Products.ToList();
                return products.Count >= 1;
            }

            public bool M4(ProductContext db)
            {
                var products = db.Products.Count();
                return products >= 1;
            }
        }

        class HasProductsWithPredicate
        {
            public bool M0(ProductContext db)
            {
                var products = db.Products.FirstOrDefault(e => e.Id == 42);
                return products is not null;
            }

            public bool M1(ProductContext db)
            {
                var products = db.Products.Count(e => e.Id == 42);
                return products > 0;
            }

            public bool M2(ProductContext db)
            {
                var products = db.Products.Count(e => e.Id == 42);
                return products >= 1;
            }
        }

        class HasNoProducts
        {
            public bool M0(ProductContext db)
            {
                var products = db.Products.FirstOrDefault();
                return products is null;
            }

            public bool M1(ProductContext db)
            {
                var products = db.Products.ToList();
                return products.Count == 0;
            }

            public bool M2(ProductContext db)
            {
                var products = db.Products.Count();
                return products == 0;
            }

            public bool M3(ProductContext db)
            {
                var products = db.Products.ToList();
                return products.Count <= 0;
            }

            public bool M4(ProductContext db)
            {
                var products = db.Products.Count();
                return products <= 0;
            }

            public bool M5(ProductContext db)
            {
                var products = db.Products.ToList();
                return products.Count < 1;
            }

            public bool M6(ProductContext db)
            {
                var products = db.Products.Count();
                return products < 1;
            }
        }

        class HasNoProductsWithPredicate
        {
            public bool M0(ProductContext db)
            {
                var products = db.Products.FirstOrDefault(e => e.Id == 42);
                return products is null;
            }

            public bool M1(ProductContext db)
            {
                var products = db.Products.Count(e => e.Id == 42);
                return products == 0;
            }

            public bool M2(ProductContext db)
            {
                var products = db.Products.Count(e => e.Id == 42);
                return products <= 0;
            }

            public bool M3(ProductContext db)
            {
                var products = db.Products.Count(e => e.Id == 42);
                return products < 1;
            }
        }

        class ProductCount
        {
            public int M0(ProductContext db)
            {
                var products = db.Products.ToList();
                return products.Count;
            }
        }

        class ProductContext(DbContextOptions<ProductContext> options) : DbContext(options)
        {
            public DbSet<Product> Products { get; set; }
        }

        record Product(int Id);
        """;

    [Theory]
    // FirstOrDefault stored in a local and checked for null: EFD002, medium confidence.
    [InlineData("HasProducts.M0", "EFD002", "medium", "Use Any with the same predicate")]
    [InlineData("HasProductsWithPredicate.M0", "EFD002", "medium", "Use Any with the same predicate")]
    [InlineData("HasNoProducts.M0", "EFD002", "medium", "Use Any with the same predicate")]
    [InlineData("HasNoProductsWithPredicate.M0", "EFD002", "medium", "Use Any with the same predicate")]
    // Count stored in a local and compared for existence: EFD002, high confidence.
    [InlineData("HasProducts.M2", "EFD002", "high", "Use Any for this existence test")]
    [InlineData("HasProducts.M4", "EFD002", "high", "Use Any for this existence test")]
    [InlineData("HasProductsWithPredicate.M1", "EFD002", "high", "Use Any for this existence test")]
    [InlineData("HasProductsWithPredicate.M2", "EFD002", "high", "Use Any for this existence test")]
    [InlineData("HasNoProducts.M2", "EFD002", "high", "Use the negation of Any for this existence test")]
    [InlineData("HasNoProducts.M4", "EFD002", "high", "Use the negation of Any for this existence test")]
    [InlineData("HasNoProducts.M6", "EFD002", "high", "Use the negation of Any for this existence test")]
    [InlineData("HasNoProductsWithPredicate.M1", "EFD002", "high", "Use the negation of Any for this existence test")]
    [InlineData("HasNoProductsWithPredicate.M2", "EFD002", "high", "Use the negation of Any for this existence test")]
    [InlineData("HasNoProductsWithPredicate.M3", "EFD002", "high", "Use the negation of Any for this existence test")]
    // ToList stored in a local and used only for its count: EFD019.
    [InlineData("HasProducts.M1", "EFD019", "high", "Apply 'Any' to the query")]
    [InlineData("HasProducts.M3", "EFD019", "high", "Apply 'Any' to the query")]
    [InlineData("HasNoProducts.M1", "EFD019", "high", "Apply 'Any' to the query")]
    [InlineData("HasNoProducts.M3", "EFD019", "high", "Apply 'Any' to the query")]
    [InlineData("HasNoProducts.M5", "EFD019", "high", "Apply 'Any' to the query")]
    [InlineData("ProductCount.M0", "EFD019", "high", "Apply 'Count' to the query")]
    public async Task EveryProposedShapeGetsExactlyOneFinding(string method, string rule, string confidence, string remediationStart)
    {
        var findings = await FindingsByMethodAsync();

        var diagnostic = Assert.Single(findings[method]);
        Assert.Equal(rule, diagnostic.Id);
        Assert.Equal(confidence, diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.StartsWith(remediationStart, diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFileCompilesAndHasTwentyMethods()
    {
        Assert.Empty(Compile().GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Equal(20, (await FindingsByMethodAsync()).Count);
    }

    private static Compilation Compile() =>
        AnalyzerTestHarness.CreateCompilation(Source, true, false, false, false, false, null, CountUsedForExistenceAnalyzer.DiagnosticId);

    // The diagnostics of each `Class.Method`, found by the source line they are reported on.
    private static async Task<Dictionary<string, List<Diagnostic>>> FindingsByMethodAsync()
    {
        var diagnostics = await Compile()
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new CountUsedForExistenceAnalyzer(), new MaterializeThenReduceAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        var byLine = diagnostics.ToLookup(static diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line);

        var findings = new Dictionary<string, List<Diagnostic>>(StringComparer.Ordinal);
        string? type = null;
        string? method = null;
        var lines = Source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (Regex.Match(lines[index], @"^class (\w+)") is { Success: true } typeMatch)
            {
                type = typeMatch.Groups[1].Value;
            }
            else if (Regex.Match(lines[index], @"^\s+public \w+ (M\d)\(") is { Success: true } methodMatch)
            {
                method = $"{type}.{methodMatch.Groups[1].Value}";
                findings[method] = [];
            }

            if (method is not null)
            {
                findings[method].AddRange(byLine[index]);
            }
        }

        return findings;
    }
}
