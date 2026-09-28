using EFDoctor.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace EFDoctor.Analyzers.Tests;

// Exercises the shared chain proof directly: each source marks the analyzed terminal call with
// `Probe(...)`, and the test proves the source of the call inside it.
public sealed class EfQueryLocalsTests
{
    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Query stored in a local and then executed")]
    public void QueryStoredInALocalIsTracedToItsOrigin()
    {
        var analysis = Prove("""
            var query = context.Items.Where(item => item.Active);
            return Probe(query.ToList());
            """);

        Assert.NotNull(analysis);
        Assert.Equal("context.Items", analysis.Origin.Syntax.ToString());
        Assert.Equal("Queryable.Where -> local 'query'", Chain(analysis));
    }

    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Straight-line recomposition")]
    public void StraightLineRecompositionAppliesEveryEarlierWriteInOrder()
    {
        var analysis = Prove("""
            var query = context.Items.Where(item => item.Active);
            query = query.OrderBy(item => item.Id);
            query = query.Take(5);
            return Probe(query.ToList());
            """);

        Assert.NotNull(analysis);
        Assert.Equal("Queryable.Where -> local 'query' -> Queryable.OrderBy -> local 'query' -> Queryable.Take -> local 'query'", Chain(analysis));
        Assert.Equal(RowBoundKind.Take, analysis.RowBound.Kind);
    }

    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Straight-line recomposition")]
    public void WriteAfterTheReadDoesNotApply()
    {
        var analysis = Prove("""
            var query = context.Items.Where(item => item.Active);
            var all = Probe(query.ToList());
            query = query.Take(1);
            return all;
            """);

        Assert.NotNull(analysis);
        Assert.Equal("Queryable.Where -> local 'query'", Chain(analysis));
        Assert.Equal(RowBoundKind.None, analysis.RowBound.Kind);
    }

    [Fact]
    [Trait("Spec", "query-chain-local-tracking/Chain through several locals")]
    public void ChainIsTracedThroughSeveralLocals()
    {
        var analysis = Prove("""
            IQueryable<Item> all = context.Items;
            var active = all.Where(item => item.Active);
            return Probe(active.ToList());
            """);

        Assert.NotNull(analysis);
        Assert.Equal("local 'all' -> Queryable.Where -> local 'active'", Chain(analysis));
    }

    [Fact]
    public void ChainStopsAfterTheHopLimit()
    {
        static string Locals(int count) =>
            "IQueryable<Item> q0 = context.Items;\n"
            + string.Concat(Enumerable.Range(1, count - 1).Select(static index => $"var q{index} = q{index - 1};\n"))
            + $"return Probe(q{count - 1}.ToList());";

        Assert.NotNull(Prove(Locals(EfQueryLocals.MaxHops)));
        Assert.Null(Prove(Locals(EfQueryLocals.MaxHops + 1)));
    }

    [Theory]
    [Trait("Spec", "query-chain-local-tracking/Conditional recomposition is not followed")]
    [InlineData("if (flag) { query = query.Take(1); }")]
    [InlineData("if (flag) query = query.Take(1); else query = query.Take(2);")]
    [InlineData("foreach (var id in ids) { query = query.Where(item => item.Id != id); }")]
    [InlineData("{ query = query.Take(1); }")]
    public void LocalWrittenOutsideItsDeclaringBlockIsNotFollowed(string write)
    {
        Assert.Null(Prove($$"""
            var query = context.Items.Where(item => item.Active);
            {{write}}
            return Probe(query.ToList());
            """));
    }

    [Theory]
    [Trait("Spec", "query-chain-local-tracking/Read inside a lambda is not followed")]
    [InlineData("Func<List<Item>> load = () => Probe(query.ToList()); return load();")]
    [InlineData("return Load(); List<Item> Load() => Probe(query.ToList());")]
    public void ReadInsideALambdaOrLocalFunctionIsNotFollowed(string use)
    {
        Assert.Null(Prove($$"""
            var query = context.Items.Where(item => item.Active);
            {{use}}
            """));
    }

    [Theory]
    [Trait("Spec", "query-chain-local-tracking/Local written through a reference is not followed")]
    [InlineData("Touch(ref query);")]
    [InlineData("Replace(out query);")]
    [InlineData("(query, var count) = (query.Take(1), 1);")]
    [InlineData("query ??= context.Items;")]
    [InlineData("Action reset = () => query = context.Items;")]
    [InlineData("ref var alias = ref query;")]
    public void LocalWrittenThroughAReferenceIsNotFollowed(string write)
    {
        Assert.Null(Prove($$"""
            var query = context.Items.Where(item => item.Active);
            {{write}}
            return Probe(query.ToList());
            """));
    }

    [Fact]
    public void LocalWithoutAnInitializerIsNotFollowed()
    {
        Assert.Null(Prove("""
            IQueryable<Item> query;
            query = context.Items.Where(item => item.Active);
            return Probe(query.ToList());
            """));
    }

    private static string Chain(EfQueryChainAnalysis analysis) => string.Join(" -> ", analysis.Operations);

    private static EfQueryChainAnalysis? Prove(string body)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.EntityFrameworkCore;

            sealed class Item
            {
                public int Id { get; set; }
                public bool Active { get; set; }
            }

            sealed class TestContext : DbContext
            {
                public DbSet<Item> Items => Set<Item>();
            }

            static class Subject
            {
                static List<Item> Run(TestContext context, bool flag, int[] ids)
                {
                    {{body}}
                }

                static T Probe<T>(T value) => value;
                static void Touch(ref IQueryable<Item> query) { }
                static void Replace(out IQueryable<Item> query) => query = Enumerable.Empty<Item>().AsQueryable();
            }
            """;
        var compilation = AnalyzerTestHarness.CreateCompilation(source, true, false, false, false, false, null, SaveChangesInLoopAnalyzer.DiagnosticId);
        Assert.Empty(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var tree = compilation.SyntaxTrees.Single();
        var probe = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(static invocation => invocation.Expression.ToString() == "Probe");
        var terminal = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(probe.ArgumentList.Arguments[0].Expression)!;

        return EfQueryOperationAnalysis.TryAnalyzeSource(
            EfQueryOperationAnalysis.GetInvocationSource(terminal)!,
            compilation.GetTypeByMetadataName("System.Linq.Queryable")!,
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbSet`1")!,
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.DbContext")!,
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions")!,
            out var analysis)
            ? analysis
            : null;
    }
}
