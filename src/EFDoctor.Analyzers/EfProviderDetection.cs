using Microsoft.CodeAnalysis;

namespace EFDoctor.Analyzers;

internal static class EfProviderDetection
{
    internal const string SqlServerMarkerMetadataName = "Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions";
    internal const string NpgsqlMarkerMetadataName = "Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions";
    internal const string MySqlMarkerMetadataName = "Microsoft.EntityFrameworkCore.MySqlDbContextOptionsBuilderExtensions";

    private static readonly ProviderInfo SqlServer = new(
        "SQL Server",
        "Microsoft.EntityFrameworkCore.SqlServer");

    private static readonly ProviderInfo Npgsql = new(
        "PostgreSQL (Npgsql)",
        "Npgsql.EntityFrameworkCore.PostgreSQL");

    internal static DetectionResult Detect(Compilation compilation)
    {
        var hasAutoIndexingProvider = compilation.GetTypeByMetadataName(MySqlMarkerMetadataName) is not null;
        if (hasAutoIndexingProvider)
        {
            return new DetectionResult(null, hasAutoIndexingProvider: true);
        }

        if (compilation.GetTypeByMetadataName(SqlServerMarkerMetadataName) is not null)
        {
            return new DetectionResult(SqlServer, hasAutoIndexingProvider: false);
        }

        if (compilation.GetTypeByMetadataName(NpgsqlMarkerMetadataName) is not null)
        {
            return new DetectionResult(Npgsql, hasAutoIndexingProvider: false);
        }

        return new DetectionResult(null, hasAutoIndexingProvider: false);
    }

    internal sealed class ProviderInfo
    {
        internal ProviderInfo(string name, string identity)
        {
            Name = name;
            Identity = identity;
        }

        internal string Name { get; }

        internal string Identity { get; }
    }

    internal sealed class DetectionResult
    {
        internal DetectionResult(ProviderInfo? eligibleProvider, bool hasAutoIndexingProvider)
        {
            EligibleProvider = eligibleProvider;
            HasAutoIndexingProvider = hasAutoIndexingProvider;
        }

        internal ProviderInfo? EligibleProvider { get; }

        internal bool HasAutoIndexingProvider { get; }
    }
}
