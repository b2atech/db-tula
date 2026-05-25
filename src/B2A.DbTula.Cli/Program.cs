using B2A.DbTula.Cli;
using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Cli.Helpers;
using B2A.DbTula.Cli.Reports;
using B2A.DbTula.Cli.Services;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            var argsParsed = CliOptions.Parse(args);

            if (!argsParsed.IsValid)
            {
                Console.WriteLine(argsParsed.GetUsage());
                return;
            }

            var unifiedLogger = LoggerHelpers.CreateUnifiedLogger();

            if (argsParsed.IsBatchMode)
            {
                // Batch mode - process multiple databases
                Log.Logger.Information("Starting batch mode from configuration file: {ConfigFile}", argsParsed.BatchConfigFile);

                var batchConfig = await BatchProcessor.LoadBatchConfigurationAsync(argsParsed.BatchConfigFile);
                if (batchConfig == null)
                {
                    Log.Logger.Fatal("Failed to load batch configuration file");
                    return;
                }

                await BatchProcessor.ProcessBatchAsync(batchConfig, argsParsed.TestMode, argsParsed.TestObjectLimit);
            }
            else if (argsParsed.IsExtract)
            {
                // Extraction mode
                var provider = SchemaProviderFactory.Create(
                    argsParsed.ExtractType,
                    argsParsed.ExtractConnectionString,
                    unifiedLogger,
                    verbose: true,
                    logLevel: LogLevel.Basic);

                var extractor = new DbSchemaExtractor(provider);

                var types = argsParsed.ExtractObjectTypes;

                // Use logging in extraction
                var objects = await extractor.ExtractAllAsync(
                    types,
                    argsParsed.TestMode ? argsParsed.TestObjectLimit : (int?)null,
                    msg => Log.Logger.Information(msg)
                );

                // Write extracted objects to separate directories per type/object name/hash, with progress logging
                DbSchemaExtractor.WriteToDirectory(
                    objects,
                    argsParsed.OutputDir,
                    msg => Log.Logger.Information(msg)
                );

                Log.Logger.Information("✅ Extraction completed. Objects written to {OutputDir}.", argsParsed.OutputDir);
            }
            else
            {
                // Comparison mode (backward compatible)
                var comparer = new SchemaComparer();

                var sourceProvider = SchemaProviderFactory.Create(
                    argsParsed.SourceType,
                    argsParsed.SourceConnectionString,
                    unifiedLogger,
                    verbose: true,
                    logLevel: LogLevel.Basic);

                var targetProvider = SchemaProviderFactory.Create(
                    argsParsed.TargetType,
                    argsParsed.TargetConnectionString,
                    unifiedLogger,
                    verbose: true,
                    logLevel: LogLevel.Basic);

                var comparisonOptions = new ComparisonOptions
                {
                    IgnoreOwnership = argsParsed.IgnoreOwnership,
                    SourceLabel = argsParsed.SourceLabel,
                    TargetLabel = argsParsed.TargetLabel
                };

                Log.Logger.Information("📊 Source ({SourceLabel}): {Source}", argsParsed.SourceLabel, argsParsed.SourceConnectionString);
                Log.Logger.Information("📊 Target ({TargetLabel}): {Target}", argsParsed.TargetLabel, argsParsed.TargetConnectionString);

                var comparisonResults = await comparer.CompareAsync(
                    sourceProvider,
                    targetProvider,
                    unifiedLogger,
                    argsParsed.TestMode,
                    argsParsed.TestObjectLimit,
                    comparisonOptions);

                var resultList = comparisonResults.ToList();

                Log.Logger.Information("📋 Fetched {Total} schema objects", resultList.Count);
                Log.Logger.Information("  ✅ Match:           {Match}", resultList.Count(r => r.Status == B2A.DbTula.Core.Enums.ComparisonStatus.Match));
                Log.Logger.Information("  ⚠️  Mismatch:        {Mismatch}", resultList.Count(r => r.Status == B2A.DbTula.Core.Enums.ComparisonStatus.Mismatch));
                Log.Logger.Information("  ❌ Missing in {TargetLabel}: {MissingTarget}", argsParsed.TargetLabel, resultList.Count(r => r.Status == B2A.DbTula.Core.Enums.ComparisonStatus.MissingInTarget));
                Log.Logger.Information("  ❌ Missing in {SourceLabel}: {MissingSource}", argsParsed.SourceLabel, resultList.Count(r => r.Status == B2A.DbTula.Core.Enums.ComparisonStatus.MissingInSource));

                var byType = resultList
                    .GroupBy(r => r.ObjectType)
                    .Select(g => $"{g.Key}={g.Count()}(diff={g.Count(x => x.Status != B2A.DbTula.Core.Enums.ComparisonStatus.Match)})");
                Log.Logger.Information("  By type: {Types}", string.Join(", ", byType));

                var report = new SchemaComparisonReport
                {
                    Title = argsParsed.Title,
                    SourceLabel = argsParsed.SourceLabel,
                    TargetLabel = argsParsed.TargetLabel,
                    GeneratedOn = DateTime.UtcNow,
                    Results = resultList
                };
                await HtmlReportGenerator.GenerateWithRazorAsync(report, argsParsed.OutputFile);

                Log.Logger.Information("✅ Report written to: {OutputFile}", argsParsed.OutputFile);
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Fatal(ex, "❌ Error during operation");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}