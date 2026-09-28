using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers.Tests;

public sealed class UnboundedQueryMaterializationAnalyzerTests
{
    public static IEnumerable<object[]> PositiveCases()
    {
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.ToList();");
        yield return Case("static async Task<List<Entity>> Run(TestContext context) => await context.Entities.ToListAsync();");
        yield return Case("static Task<List<Entity>> Run(TestContext context) => context.Entities.ToListAsync();");
        yield return Case("static List<Entity> Run(TestContext context) => Enumerable.ToList(context.Entities);");
        yield return Case("static Task<List<Entity>> Run(TestContext context) => EntityFrameworkQueryableExtensions.ToListAsync(context.Entities);");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.Where(entity => entity.Active).ToList();");
        yield return Case("static List<string?> Run(TestContext context) => context.Entities.Select(entity => entity.Name).ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.OrderBy(entity => entity.Name).Skip(10).ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.Distinct().ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.Include(entity => entity.Children).ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.AsNoTracking().ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.AsSplitQuery().ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.AsSingleQuery().ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.AsNoTrackingWithIdentityResolution().ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.TagWith(\"review\").ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Set<Entity>().ToList();");
        yield return Case("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.ProductId == id).ToList();");
        yield return Case("static List<Entity> Run(TestContext context, DateTime since) => context.Entities.Where(entity => entity.CreatedUtc >= since).ToList();");
        yield return Case("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where(entity => IsGood(entity)); static bool IsGood(Entity entity) => true;");
        yield return Case("static List<Entity> Run(TestContext context, DateTime since) => context.Entities.Where(entity => entity.Name == \"CreateTimeEntry\" && entity.CreatedUtc >= since).ToList();");
        yield return Case("static List<Entity> Run(TestContext context, int value) => context.Entities.Where(entity => entity.IndexedValue == value).ToList();");
        yield return Case("static List<Entity> Run(TestContext context, Entity product) => context.Entities.Where(entity => entity.ProductId == product.ProductId).ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.Where(entity => entity.MetadataKey == entity.ProductId).ToList();");
        yield return Case("static List<Entity> Run(TestContext context) => context.Entities.Where(entity => entity.MetadataKey.Equals(entity.ProductId)).ToList();");
        yield return Case("static Entity Run(TestContext context) => context.Entities.ToList().First(entity => IsGood(entity)); static bool IsGood(Entity entity) => true;");
        yield return new object[]
        {
            QuerySource("""
                static object Run(TestContext context)
                {
                    var first = context.Entities.ToList();
                    var second = context.Entities.Where(entity => entity.Id > 0).ToListAsync();
                    return new object[] { first, second };
                }
                """),
            2,
        };
    }

    public static IEnumerable<object[]> NegativeCases()
    {
        yield return SourceCase("static List<Entity> Run(TestContext context) => context.Entities.Take(100).ToList();");
        yield return SourceCase("static Task<List<Entity>> Run(TestContext context, int limit) => context.Entities.Take(limit).ToListAsync();");
        yield return SourceCase("static List<string?> Run(TestContext context) => context.Entities.Take(10).Where(entity => entity.Active).Select(entity => entity.Name).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.Where(entity => ids.Contains(entity.ProductId)).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.Where(entity => ids.Any(id => id == entity.ProductId)).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context) => context.Entities.Where(entity => FieldIds.Contains(entity.ProductId)).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.MetadataKey == id).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.ParentKey == id).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, string code) => context.Entities.Where(entity => entity.AlternateKey == code).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, Entity other) => context.Entities.Where(entity => entity.MetadataKey == other.MetadataKey).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, Entity other) => context.Entities.Where(entity => entity.ParentKey == other.ParentKey).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, Entity product) => context.Entities.Where(entity => entity.ParentKey == product.ParentKey && entity.Active).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, Request request) => context.Entities.Where(entity => entity.MetadataKey == request.Filter.MetadataKey).ToList();", "sealed class Request { public Filter Filter { get; set; } = new(); } sealed class Filter { public int MetadataKey { get; set; } }");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.Where(entity => ids.Contains(entity.ProductId) && entity.Active).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.Where(entity => entity.Active && ids.Contains(entity.ProductId) && entity.Name != null).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.AsNoTracking().Where(entity => entity.OptionalProductId.HasValue && ids.Contains(entity.OptionalProductId.Value)).Include(entity => entity.Parent).ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.Where(entity => ids.Contains(entity.ProductId)).AsSplitQuery().ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, int[] ids) => context.Entities.Include(entity => entity.Children).ThenInclude(child => child.Children).Where(entity => ids.Contains(entity.ProductId)).AsNoTracking().TagWith(\"hydrate\").ToList();");
        yield return SourceCase("static Entity Run(TestContext context) => context.Entities.ToList().First();");
        yield return SourceCase("static List<Entity> Run(TestContext context) => context.Entities.Take(\"page\").ToList();");
        yield return SourceCase("static List<int> Run() => new[] { 1, 2 }.ToList();");
        yield return SourceCase("static List<Entity> Run(IQueryable<Entity> query) => query.ToList();");
        yield return SourceCase("static List<Entity> Run(TestContext context, bool active) { var query = context.Entities.Where(entity => entity.Active); if (active) { query = query.Take(100); } return query.ToList(); }");
        yield return SourceCase("static List<Entity> Run(TestContext context) => context.Entities.AsEnumerable().ToList();");
        yield return SourceCase("static Entity[] Run(TestContext context) => context.Entities.ToArray();");
        yield return SourceCase("static List<Entity> Run(Store store) => store.ToList();", "sealed class Store { public List<Entity> ToList() => new(); }");
        yield return SourceCase("static object Run(TestContext context) => missing.ToList();");
        yield return SourceCase("static object Run(TestContext context) { /* context.Entities.ToList() */ var text = \"ToListAsync\"; #if NEVER return context.Entities.ToList(); #else return text; #endif }");
        yield return SourceCase("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where(entity => entity.Id > 0);");
        yield return SourceCase("static IEnumerable<string?> Run(TestContext context) => context.Entities.ToList().Select(entity => entity.Name);");
        yield return SourceCase("static async Task<IEnumerable<Entity>> Run(TestContext context) => (await context.Entities.ToListAsync()).Take(5);");
        yield return new object[] { "// <auto-generated />\n" + QuerySource("static List<Entity> Run(TestContext context) => context.Entities.ToList();") };
    }

    [Theory]
    [MemberData(nameof(PositiveCases))]
    [Trait("Spec", "efd005-unbounded-query-materialization/Direct DbSet ToList")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Composed EF query ToListAsync")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Static materializer syntax")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Direct unbounded materialization")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Filtered query without Take")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Projection ordering or Skip without Take")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Include or tracking modifier without Take")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Multiple unbounded materializations")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Skip without Take")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Non-unique or composite index is not a strong key bound")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Both sides are entity property paths")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Equals between two entity properties")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Terminal unbounded ToList")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Unsupported client composition")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Unsupported reduction")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Analyzer fixture suite")]
    public async Task ReportsPositiveFixture(string source, int expectedCount)
    {
        Assert.Empty(AnalyzerTestHarness.GetCompilationDiagnostics(source).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var matches = (await AnalyzeAsync(source)).Where(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);
        Assert.Equal(expectedCount, matches.Count());
    }

    [Theory]
    [MemberData(nameof(NegativeCases))]
    [Trait("Spec", "efd005-unbounded-query-materialization/In-memory or arbitrary query source")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Unrelated or unresolved materializer")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Strong predicate bound present")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Constant Take bound")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Parameterized Take bound")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Take before later query composition")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Local collection membership")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Membership via Any")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Membership within a conjunction")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Null-guarded membership over a nullable key")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality bound from metadata")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality against a property of another entity")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality against a nested member of a parameter")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality with a compound predicate and a property comparand")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Include and tracking modifiers are bound-neutral")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Explicit AsEnumerable boundary")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Arbitrary IQueryable parameter")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Conditionally composed stored query")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Comments strings and inactive code")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Immediate reduction after ToList")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Unrelated Take method")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Analyzer fixture suite")]
    public async Task DoesNotReportNegativeFixture(string source)
    {
        Assert.True(
            !(await AnalyzeAsync(source)).Any(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId),
            source);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/Immediate SQL-capable filtering after ToList")]
    public async Task Efd004EligibleMaterializerProducesOnlyTheSpecificDiagnostic()
    {
        var source = QuerySource("static IEnumerable<Entity> Run(TestContext context) => context.Entities.ToList().Where(entity => entity.Id > 0);");

        var efd004 = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new PrematureQueryMaterializationAnalyzer(),
            diagnosticId: PrematureQueryMaterializationAnalyzer.DiagnosticId);
        var efd005 = await AnalyzeAsync(source);

        Assert.Single(efd004, static diagnostic => diagnostic.Id == PrematureQueryMaterializationAnalyzer.DiagnosticId);
        Assert.DoesNotContain(efd005, static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/Complete structured finding")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Exact diagnostic location")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Qualified impact language")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Semantics-preserving remediation")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Legitimate full-result guidance")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Unknown context defaults to medium")]
    public async Task ReportsExactLocationsAndCompleteQualifiedProperties()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var first = context.Entities.ToList();
                var second = context.Entities.Where(entity => entity.Active).ToListAsync();
                return new object[] { first, second };
            }
            """);

        var matches = (await AnalyzeAsync(source)).Where(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId).ToArray();

        Assert.Equal(2, matches.Length);
        Assert.Equal("context.Entities.ToList()", Text(matches[0]));
        Assert.Equal("context.Entities.Where(entity => entity.Active).ToListAsync()", Text(matches[1]));
        Assert.All(matches, static diagnostic =>
        {
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("medium", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
            Assert.Contains("DbSet origin", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
            Assert.Contains("recognized row bound is None (None)", diagnostic.Properties[DiagnosticPropertyNames.Evidence]);
            Assert.Contains("actual impact depends", diagnostic.Properties[DiagnosticPropertyNames.LikelyImpact], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hydrating children for a known set of parents", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
            Assert.DoesNotContain("Do not add an arbitrary limit", diagnostic.Properties[DiagnosticPropertyNames.SuggestedRemediation], StringComparison.Ordinal);
            Assert.Equal("EFD005", diagnostic.Properties[DiagnosticPropertyNames.DocumentationReference]);
        });
        Assert.Contains("Queryable.Where", matches[1].Properties[DiagnosticPropertyNames.Evidence]);
    }

    [Theory]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality bound by name heuristic")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Medium bound downgrade")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Weak bound downgrade")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Time-window intent signal")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Hot-path unbounded query")]
    [InlineData("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.ProductId == id).ToList();", "advisory", "KeyEqualityByName (Medium)")]
    [InlineData("static List<Entity> Run(TestContext context, Entity product) => context.Entities.Where(entity => entity.ProductId == product.ProductId).ToList();", "advisory", "KeyEqualityByName (Medium)")]
    [InlineData("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.ProductId == id).ToList();", "advisory", "KeyEqualityByName (Medium)", "CustomerService")]
    [InlineData("static List<Entity> Run(TestContext context, DateTime since) => context.Entities.Where(entity => entity.CreatedUtc >= since).ToList();", "advisory", "TimeWindow (Weak)")]
    [InlineData("static List<Entity> Run(TestContext context, DateTime since) => context.Entities.Where(entity => entity.Name == \"CreateTimeEntry\" && entity.CreatedUtc >= since).ToList();", "advisory", "TimeWindow (Weak)")]
    [InlineData("static List<Entity> Run(TestContext context) => context.Entities.ToList();", "high", "None (None)", "CustomerService")]
    public async Task AssignsConfidenceFromBoundAndCallSite(
        string member,
        string expectedConfidence,
        string expectedBound,
        string containingType = "Subject")
    {
        var diagnostic = Assert.Single(await AnalyzeAsync(QuerySource(member, containingType: containingType)));

        Assert.Equal(expectedConfidence, diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains(expectedBound, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality through a user-defined equality operator")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality through a lifted nullable operator")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key-equality through Equals")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Membership via Any with a user-defined equality operator")]
    [InlineData("static List<Entity> Run(TestContext context, Guid id) => context.Entities.Where(entity => entity.GuidKey == id).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, Guid id) => context.Entities.Where(entity => id == entity.GuidKey).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, Guid id) => context.Entities.Where(entity => entity.OptionalGuidKey == id).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, Entity other) => context.Entities.Where(entity => entity.GuidKey == other.GuidKey && entity.Active).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, Guid[] ids) => context.Entities.Where(entity => ids.Any(id => id == entity.GuidKey)).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.MetadataKey.Equals(id)).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => id.Equals(entity.MetadataKey)).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, Guid id) => context.Entities.Where(entity => entity.GuidKey.Equals(id)).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, object id) => context.Entities.Where(entity => entity.MetadataKey.Equals(id)).ToList();", null)]
    [InlineData("static List<Entity> Run(TestContext context, Guid id) => context.Entities.Where(entity => entity.ExternalId == id).ToList();", "KeyEqualityByName (Medium)")]
    [InlineData("static List<Entity> Run(TestContext context, Guid? id) => context.Entities.Where(entity => entity.OptionalExternalId == id).ToList();", "KeyEqualityByName (Medium)")]
    [InlineData("static List<Entity> Run(TestContext context, int id) => context.Entities.Where(entity => entity.ProductId.Equals(id)).ToList();", "KeyEqualityByName (Medium)")]
    [InlineData("static List<Entity> Run(TestContext context, Guid id) => context.Entities.Where(entity => id.Equals(entity.ExternalId)).ToList();", "KeyEqualityByName (Medium)")]
    public async Task RecognizesKeyEqualityForms(string member, string? expectedAdvisoryBound)
    {
        var source = QuerySource(member);
        Assert.Empty(AnalyzerTestHarness.GetCompilationDiagnostics(source).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = (await AnalyzeAsync(source)).Where(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId).ToArray();

        if (expectedAdvisoryBound is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("advisory", diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains(expectedAdvisoryBound, diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd005-unbounded-query-materialization/Key from fluent configuration")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Foreign key from fluent configuration")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Unique index from fluent configuration")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Composite or non-unique fluent configuration is not a strong key")]
    [InlineData("modelBuilder.Entity<Account>().HasKey(account => account.Code);", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.Code == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasKey(\"Code\");", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.Code == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasKey(nameof(Account.Code));", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.Code == value).ToList();", null, "")]
    [InlineData("", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.Code == value).ToList();", null, "sealed class AccountConfiguration : IEntityTypeConfiguration<Account> { public void Configure(EntityTypeBuilder<Account> builder) => builder.HasKey(account => account.Code); }")]
    [InlineData("modelBuilder.Entity<Account>(builder => builder.HasAlternateKey(account => account.LegacyCode));", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.LegacyCode == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<BaseRecord>().HasAlternateKey(record => record.SiteKey);", "static List<Record> Run(TestContext context, int value) => context.Records.Where(record => record.SiteKey == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasOne(account => account.Owner).WithMany(owner => owner.Accounts).HasForeignKey(account => account.OwnerRef);", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.OwnerRef == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Owner>().HasMany(owner => owner.Accounts).WithOne(account => account.Owner).HasForeignKey(account => account.OwnerRef);", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.OwnerRef == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasOne(account => account.Owner).WithMany(owner => owner.Accounts).HasForeignKey(\"OwnerRef\");", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.OwnerRef == value).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasIndex(account => account.Email).IsUnique();", "static List<Account> Run(TestContext context, string email) => context.Accounts.Where(account => account.Email == email).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasIndex(account => account.Email).HasDatabaseName(\"IX_Email\").IsUnique(true);", "static List<Account> Run(TestContext context, string email) => context.Accounts.Where(account => account.Email == email).ToList();", null, "")]
    [InlineData("modelBuilder.Entity<Account>().HasKey(account => new { account.TenantId, account.RegionId });", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TenantId == value).ToList();", "advisory", "")]
    [InlineData("modelBuilder.Entity<Account>().HasKey(\"TenantId\", \"RegionId\");", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TenantId == value).ToList();", "advisory", "")]
    [InlineData("modelBuilder.Entity<Account>().HasIndex(account => account.TenantId);", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TenantId == value).ToList();", "advisory", "")]
    [InlineData("modelBuilder.Entity<Account>().HasIndex(account => account.TenantId).IsUnique(false);", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TenantId == value).ToList();", "advisory", "")]
    [InlineData("modelBuilder.Entity<Account>().HasOne(account => account.Owner).WithMany(owner => owner.Accounts).HasForeignKey(account => new { account.TenantId, account.RegionId });", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TenantId == value).ToList();", "advisory", "")]
    [InlineData("new Account().HasKey(account => account.TenantId);", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TenantId == value).ToList();", "advisory", "static class UnrelatedBuilder { public static void HasKey(this Account account, Func<Account, int> column) { } }")]
    [InlineData("modelBuilder.Entity<Account>().HasIndex(account => account.Code).IsUnique();", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.Code == value).ToList();", null, "")]
    public async Task RecognizesKeysFromModelConfiguration(string onModelCreating, string member, string? expectedConfidence, string additionalTypes)
    {
        await AssertModelKeyOutcome(onModelCreating, member, expectedConfidence, additionalTypes);
    }

    [Theory]
    [Trait("Spec", "efd005-unbounded-query-materialization/Foreign key by navigation convention")]
    [Trait("Spec", "efd005-unbounded-query-materialization/Primary-key naming convention alone is not a proven key")]
    [InlineData("", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.OrganizationId == value).ToList();", null, "")]
    [InlineData("", "static List<Record> Run(TestContext context, int value) => context.Records.Where(record => record.CompanyId == value).ToList();", null, "")]
    [InlineData("", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.TeamId == value).ToList();", "advisory", "")]
    [InlineData("", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.LabelId == value).ToList();", "advisory", "")]
    [InlineData("", "static List<Account> Run(TestContext context, int value) => context.Accounts.Where(account => account.Id == value).ToList();", "advisory", "")]
    public async Task RecognizesForeignKeysByNavigationConvention(string onModelCreating, string member, string? expectedConfidence, string additionalTypes)
    {
        await AssertModelKeyOutcome(onModelCreating, member, expectedConfidence, additionalTypes);
    }

    private static async Task AssertModelKeyOutcome(string onModelCreating, string member, string? expectedConfidence, string additionalTypes)
    {
        var source = ModelSource(onModelCreating, member, additionalTypes);
        Assert.Empty(AnalyzerTestHarness.GetCompilationDiagnostics(source).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostics = (await AnalyzeAsync(source)).Where(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId).ToArray();

        if (expectedConfidence is null)
        {
            Assert.Empty(diagnostics);
            return;
        }

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(expectedConfidence, diagnostic.Properties[DiagnosticPropertyNames.Confidence]);
        Assert.Contains("KeyEqualityByName (Medium)", diagnostic.Properties[DiagnosticPropertyNames.Evidence], StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Spec", "efd005-unbounded-query-materialization/Tracking and filter modifiers are bound-neutral")]
    [InlineData("AsTracking()")]
    [InlineData("AsTracking(QueryTrackingBehavior.TrackAll)")]
    [InlineData("IgnoreQueryFilters()")]
    [InlineData("IgnoreQueryFilters(new[] { \"SoftDelete\" })")]
    [InlineData("IgnoreAutoIncludes()")]
    [InlineData("TagWithCallSite()")]
    public async Task TrackingAndFilterModifiersAreBoundNeutral(string modifier)
    {
        var source = QuerySource($$"""
            static object Run(TestContext context, int[] ids)
            {
                var unbounded = context.Entities.{{modifier}}.Where(entity => entity.Active).ToList();
                var bounded = context.Entities.{{modifier}}.Where(entity => ids.Contains(entity.ProductId)).ToList();
                return new object[] { unbounded, bounded };
            }
            """);
        Assert.Empty(AnalyzerTestHarness.GetCompilationDiagnostics(source).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var diagnostic = Assert.Single(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);

        Assert.Contains(".Where(entity => entity.Active).ToList()", Text(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    public void ChainProofAcceptsEveryElementPreservingEfMethod()
    {
        Assert.Empty(EfQueryOperationAnalysis.ElementPreservingEfMethods.Except(EfQueryOperationAnalysis.BoundNeutralEfMethods));
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/AsSplitQuery parity")]
    [Trait("Spec", "efd005-unbounded-query-materialization/AsSplitQuery parity on unbounded queries")]
    public async Task BoundNeutralMethodsHaveParityForBoundedAndUnboundedQueries()
    {
        var source = QuerySource("""
            static object Run(TestContext context, int[] ids)
            {
                var bounded = context.Entities.Where(entity => ids.Contains(entity.ProductId)).ToList();
                var boundedSplit = context.Entities.Where(entity => ids.Contains(entity.ProductId)).AsSplitQuery().ToList();
                var unbounded = context.Entities.Where(entity => entity.Active).ToList();
                var unboundedSplit = context.Entities.Where(entity => entity.Active).AsSplitQuery().ToList();
                return new object[] { bounded, boundedSplit, unbounded, unboundedSplit };
            }
            """);

        var diagnostics = await AnalyzeAsync(source);

        Assert.Equal(2, diagnostics.Count(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId));
    }

    [Theory]
    [Trait("Spec", "efd005-unbounded-query-materialization/Stored query proof boundary is token-independent")]
    [InlineData("context.Entities.Where(entity => entity.Active)", "")]
    [InlineData("context.Entities.Where(entity => entity.Active)", ".AsSplitQuery()")]
    [InlineData("context.Entities.Where(entity => ids.Contains(entity.ProductId))", "")]
    [InlineData("context.Entities.Where(entity => ids.Contains(entity.ProductId))", ".AsNoTracking()")]
    [InlineData("context.Entities.Take(50)", "")]
    [InlineData("context.Entities.Where(entity => entity.ProductId == ids[0])", ".Include(entity => entity.Children)")]
    public async Task StoredQueryHasTheSameOutcomeAsTheInlineQuery(string query, string boundNeutral)
    {
        var inline = QuerySource($$"""
            static object Run(TestContext context, int[] ids) => {{query}}{{boundNeutral}}.ToList();
            """);
        var stored = QuerySource($$"""
            static object Run(TestContext context, int[] ids)
            {
                var stored = {{query}};
                return stored{{boundNeutral}}.ToList();
            }
            """);

        var inlineCount = (await AnalyzeAsync(inline)).Count(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);
        var storedCount = (await AnalyzeAsync(stored)).Count(static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);

        Assert.Equal(inlineCount, storedCount);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/Query stored before materialization")]
    [Trait("Spec", "query-chain-local-tracking/Evidence names the followed local")]
    public async Task StoredQueryIsReportedWithTheLocalInItsEvidence()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
                var query = context.Entities.Where(entity => entity.Active);
                query = query.OrderBy(entity => entity.Name);
                return query.ToList();
            }
            """);

        var diagnostic = Assert.Single(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);

        Assert.Contains(
            "the inline query chain is 'Queryable.Where -> local 'query' -> Queryable.OrderBy -> local 'query''",
            diagnostic.Properties[DiagnosticPropertyNames.Evidence]!,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/Pragma suppression")]
    public async Task PragmaSuppressionPreservesUnsuppressedNeighbor()
    {
        var source = QuerySource("""
            static object Run(TestContext context)
            {
            #pragma warning disable EFD005 // Intentional full export of this dataset.
                var suppressed = context.Entities.ToList();
            #pragma warning restore EFD005
                var reported = context.Entities.AsNoTracking().ToListAsync();
                return new object[] { suppressed, reported };
            }
            """);

        Assert.Single(await AnalyzeAsync(source), static diagnostic => diagnostic.Id == UnboundedQueryMaterializationAnalyzer.DiagnosticId);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/Editor configuration suppression")]
    public async Task EditorConfigurationSeverityNoneIsHonored()
    {
        var source = QuerySource("static List<Entity> Run(TestContext context) => context.Entities.ToList();");

        var diagnostics = await AnalyzerTestHarness.AnalyzeAsync(
            source,
            configuredSeverity: ReportDiagnostic.Suppress,
            analyzer: new UnboundedQueryMaterializationAnalyzer(),
            diagnosticId: UnboundedQueryMaterializationAnalyzer.DiagnosticId);

        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "efd005-unbounded-query-materialization/SuppressMessage with justification")]
    public async Task SuppressMessageWithJustificationIsHonored()
    {
        var source = QuerySource("""
            [SuppressMessage("Performance", "EFD005", Justification = "Intentional full export processed in a controlled batch.")]
            static List<Entity> Run(TestContext context) => context.Entities.ToList();
            """);

        Assert.Empty(await AnalyzeAsync(source));
    }

    [Fact]
    public void DescriptorIsCompleteAndQualified()
    {
        Assert.Equal("EFD005", UnboundedQueryMaterializationAnalyzer.Rule.Id);
        Assert.Equal("Query materialization has no recognized row bound", UnboundedQueryMaterializationAnalyzer.Rule.Title.ToString());
        Assert.Equal(DiagnosticSeverity.Warning, UnboundedQueryMaterializationAnalyzer.Rule.DefaultSeverity);
        Assert.Equal("medium", UnboundedQueryMaterializationAnalyzer.Confidence);
        Assert.Contains("without", UnboundedQueryMaterializationAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("always", UnboundedQueryMaterializationAnalyzer.Rule.MessageFormat.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static Task<System.Collections.Immutable.ImmutableArray<Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHarness.AnalyzeAsync(
            source,
            analyzer: new UnboundedQueryMaterializationAnalyzer(),
            diagnosticId: UnboundedQueryMaterializationAnalyzer.DiagnosticId);

    private static object[] Case(string member) => [QuerySource(member), 1];

    private static object[] SourceCase(string member, string additionalTypes = "") => [QuerySource(member, additionalTypes)];

    private static string Text(Diagnostic diagnostic) => diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan);

    private static string ModelSource(string onModelCreating, string member, string additionalTypes) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        sealed class Account
        {
            public int Id { get; set; }
            public int Code { get; set; }
            public int LegacyCode { get; set; }
            public string Email { get; set; } = "";
            public int OwnerRef { get; set; }
            public Owner? Owner { get; set; }
            public int TenantId { get; set; }
            public int RegionId { get; set; }
            public int OrganizationId { get; set; }
            public Organization? Organization { get; set; }
            public int TeamId { get; set; }
            public List<Team> Team { get; set; } = new();
            public int LabelId { get; set; }
            public string Label { get; set; } = "";
        }

        sealed class Owner
        {
            public int Id { get; set; }
            public List<Account> Accounts { get; set; } = new();
        }

        sealed class Organization { public int Id { get; set; } }

        sealed class Team { public int Id { get; set; } }

        sealed class Company { public int Id { get; set; } }

        class BaseRecord
        {
            public int Id { get; set; }
            public int CompanyId { get; set; }
            public Company? Company { get; set; }
            public int SiteKey { get; set; }
        }

        sealed class Record : BaseRecord { }

        sealed class TestContext : DbContext
        {
            public DbSet<Account> Accounts => Set<Account>();
            public DbSet<Record> Records => Set<Record>();

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                {{onModelCreating}}
            }
        }

        {{additionalTypes}}

        static class Subject
        {
            {{member}}
        }
        """;

    private static string QuerySource(string member, string additionalTypes = "", string containingType = "Subject") => $$"""
        using System;
        using System.Collections.Generic;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.EntityFrameworkCore;

        [Index(nameof(AlternateKey), IsUnique = true)]
        [Index(nameof(GuidKey), IsUnique = true)]
        [Index(nameof(OptionalGuidKey), IsUnique = true)]
        [Index(nameof(ProductId), nameof(IndexedValue))]
        sealed class Entity
        {
            public int Id { get; set; }
            [System.ComponentModel.DataAnnotations.Key]
            public int MetadataKey { get; set; }
            [System.ComponentModel.DataAnnotations.Schema.ForeignKey(nameof(Parent))]
            public int ParentKey { get; set; }
            public Entity? Parent { get; set; }
            public string AlternateKey { get; set; } = "";
            public int ProductId { get; set; }
            public int? OptionalProductId { get; set; }
            public int IndexedValue { get; set; }
            public Guid GuidKey { get; set; }
            public Guid? OptionalGuidKey { get; set; }
            public Guid ExternalId { get; set; }
            public Guid? OptionalExternalId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public string? Name { get; set; }
            public bool Active { get; set; }
            public List<Entity> Children { get; set; } = new();
        }

        sealed class TestContext : DbContext
        {
            public DbSet<Entity> Entities => Set<Entity>();
        }

        static class UnrelatedQueryExtensions
        {
            public static IQueryable<T> Take<T>(this IQueryable<T> source, string label) => source;
        }

        {{additionalTypes}}

        static class {{containingType}}
        {
            private static readonly int[] FieldIds = [1, 2];
            {{member}}
        }
        """;
}
