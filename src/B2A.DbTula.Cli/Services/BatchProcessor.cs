using B2A.DbTula.Cli;
using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Cli.Helpers;
using B2A.DbTula.Cli.Models;
using B2A.DbTula.Cli.Reports;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using Serilog;
using System.Text.Json;

namespace B2A.DbTula.Cli.Services;

public class BatchProcessor
{
    public static async Task<BatchConfiguration?> LoadBatchConfigurationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Log.Logger.Error("Batch configuration file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };
            return JsonSerializer.Deserialize<BatchConfiguration>(json, options);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to parse batch configuration file: {FilePath}", filePath);
            return null;
        }
    }

    public static async Task ProcessBatchAsync(BatchConfiguration config, bool testMode = false, int testObjectLimit = 10)
    {
        var totalJobs = (config.ExtractionJobs?.Count ?? 0) + (config.ComparisonJobs?.Count ?? 0);
        var currentJob = 0;

        Log.Logger.Information("🚀 Starting batch processing with {TotalJobs} jobs", totalJobs);

        // Process extraction jobs
        if (config.ExtractionJobs != null && config.ExtractionJobs.Any())
        {
            Log.Logger.Information("📤 Processing {Count} extraction jobs", config.ExtractionJobs.Count);
            
            foreach (var job in config.ExtractionJobs)
            {
                currentJob++;
                Log.Logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log.Logger.Information("📋 Job {Current}/{Total}: {JobName}", currentJob, totalJobs, job.Name);
                Log.Logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                try
                {
                    await ProcessExtractionJobAsync(job, testMode, testObjectLimit);
                    Log.Logger.Information("✅ Extraction job '{JobName}' completed successfully", job.Name);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "❌ Extraction job '{JobName}' failed", job.Name);
                }
            }
        }

        // Process comparison jobs
        if (config.ComparisonJobs != null && config.ComparisonJobs.Any())
        {
            Log.Logger.Information("🔍 Processing {Count} comparison jobs", config.ComparisonJobs.Count);
            
            foreach (var job in config.ComparisonJobs)
            {
                currentJob++;
                Log.Logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                Log.Logger.Information("📋 Job {Current}/{Total}: {JobName}", currentJob, totalJobs, job.Name);
                Log.Logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

                try
                {
                    await ProcessComparisonJobAsync(job, testMode, testObjectLimit);
                    Log.Logger.Information("✅ Comparison job '{JobName}' completed successfully", job.Name);
                }
                catch (Exception ex)
                {
                    Log.Logger.Error(ex, "❌ Comparison job '{JobName}' failed", job.Name);
                }
            }
        }

        Log.Logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Log.Logger.Information("🎉 Batch processing completed. {Total} jobs processed.", totalJobs);
        Log.Logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    private static async Task ProcessExtractionJobAsync(ExtractionJob job, bool testMode, int testObjectLimit)
    {
        Log.Logger.Information("📦 Extracting from database: {JobName}", job.Name);
        Log.Logger.Information("   Database Type: {DbType}", job.DbType);
        Log.Logger.Information("   Output Directory: {OutputDir}", job.OutputDir);
        Log.Logger.Information("   Objects: {Objects}", job.Objects);

        if (!Enum.TryParse<DbType>(job.DbType, true, out var dbType))
        {
            throw new ArgumentException($"Invalid database type: {job.DbType}");
        }

        var unifiedLogger = LoggerHelpers.CreateUnifiedLogger();
        var provider = SchemaProviderFactory.Create(
            dbType,
            job.ConnectionString,
            unifiedLogger,
            verbose: true,
            logLevel: LogLevel.Basic);

        var extractor = new DbSchemaExtractor(provider);

        var objectTypes = job.Objects.ToLowerInvariant() == "all"
            ? new[] { "functions", "procedures", "views", "triggers", "tables" }
            : job.Objects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var objects = await extractor.ExtractAllAsync(
            objectTypes,
            testMode ? testObjectLimit : (int?)null,
            msg => Log.Logger.Information(msg)
        );

        DbSchemaExtractor.WriteToDirectory(
            objects,
            job.OutputDir,
            msg => Log.Logger.Information(msg)
        );

        Log.Logger.Information("   ✓ Objects written to {OutputDir}", job.OutputDir);
    }

    private static async Task ProcessComparisonJobAsync(ComparisonJob job, bool testMode, int testObjectLimit)
    {
        Log.Logger.Information("🔄 Comparing schemas: {JobName}", job.Name);
        Log.Logger.Information("   Source Type: {SourceType}", job.SourceType);
        Log.Logger.Information("   Target Type: {TargetType}", job.TargetType);
        Log.Logger.Information("   Output File: {OutputFile}", job.OutputFile);

        if (!Enum.TryParse<DbType>(job.SourceType, true, out var sourceType))
        {
            throw new ArgumentException($"Invalid source database type: {job.SourceType}");
        }

        if (!Enum.TryParse<DbType>(job.TargetType, true, out var targetType))
        {
            throw new ArgumentException($"Invalid target database type: {job.TargetType}");
        }

        var unifiedLogger = LoggerHelpers.CreateUnifiedLogger();
        var comparer = new SchemaComparer();

        var sourceProvider = SchemaProviderFactory.Create(
            sourceType,
            job.SourceConnectionString,
            unifiedLogger,
            verbose: true,
            logLevel: LogLevel.Basic);

        var targetProvider = SchemaProviderFactory.Create(
            targetType,
            job.TargetConnectionString,
            unifiedLogger,
            verbose: true,
            logLevel: LogLevel.Basic);

        var comparisonOptions = new ComparisonOptions
        {
            IgnoreOwnership = job.IgnoreOwnership
        };

        var comparisonResults = await comparer.CompareAsync(
            sourceProvider,
            targetProvider,
            unifiedLogger,
            testMode,
            testObjectLimit,
            comparisonOptions);

        var report = new SchemaComparisonReport
        {
            Title = job.Title,
            GeneratedOn = DateTime.UtcNow,
            Results = comparisonResults.ToList()
        };

        await HtmlReportGenerator.GenerateWithRazorAsync(report, job.OutputFile);

        Log.Logger.Information("   ✓ Report generated: {OutputFile}", job.OutputFile);
    }
}
