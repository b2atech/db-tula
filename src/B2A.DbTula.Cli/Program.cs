using B2A.DbTula.Cli;
using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Cli.Helpers;
using B2A.DbTula.Cli.Reports;
using B2A.DbTula.Cli.Services;
using B2A.DbTula.Core.Enums;
using B2A.DbTula.Core.Models;
using Serilog;
using System.Linq;

/// <summary>
/// Exit codes:
///   0 = all objects match
///   1 = mismatches or missing objects detected (drift)
///   2 = objects missing in target (destructive drift — objects exist in source but not target)
///   3 = comparison failed (connection error, query error)
/// </summary>
internal class Program
{
    private static async Task<int> Main(string[] args)
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
                return 3;
            }

            var unifiedLogger = LoggerHelpers.CreateUnifiedLogger();

            if (argsParsed.IsBatchMode)
            {
                Log.Logger.Information("Starting batch mode from configuration file: {ConfigFile}", argsParsed.BatchConfigFile);
                var batchConfig = await BatchProcessor.LoadBatchConfigurationAsync(argsParsed.BatchConfigFile);
                if (batchConfig == null)
                {
                    Log.Logger.Fatal("Failed to load batch configuration file");
                    return 3;
                }
                await BatchProcessor.ProcessBatchAsync(batchConfig, argsParsed.TestMode, argsParsed.TestObjectLimit);
                return 0;
            }

            if (argsParsed.IsExtract)
            {
                var provider = SchemaProviderFactory.Create(
                    argsParsed.ExtractType, argsParsed.ExtractConnectionString,
                    unifiedLogger, verbose: true, logLevel: LogLevel.Basic);

                var extractor = new DbSchemaExtractor(provider);
                var objects = await extractor.ExtractAllAsync(
                    argsParsed.ExtractObjectTypes,
                    argsParsed.TestMode ? argsParsed.TestObjectLimit : (int?)null,
                    msg => Log.Logger.Information(msg));

                DbSchemaExtractor.WriteToDirectory(objects, argsParsed.OutputDir,
                    msg => Log.Logger.Information(msg));

                Log.Logger.Information("✅ Extraction completed. Objects written to {OutputDir}.", argsParsed.OutputDir);
                return 0;
            }

            // ── Comparison mode ───────────────────────────────────────────────
            var comparer = new SchemaComparer();

            var sourceProvider = SchemaProviderFactory.Create(
                argsParsed.SourceType, argsParsed.SourceConnectionString,
                unifiedLogger, verbose: true, logLevel: LogLevel.Basic);

            var targetProvider = SchemaProviderFactory.Create(
                argsParsed.TargetType, argsParsed.TargetConnectionString,
                unifiedLogger, verbose: true, logLevel: LogLevel.Basic);

            var comparisonOptions = new ComparisonOptions
            {
                IgnoreOwnership = argsParsed.IgnoreOwnership,
                SourceLabel     = argsParsed.SourceLabel,
                TargetLabel     = argsParsed.TargetLabel
            };

            Log.Logger.Information("📊 Source ({SourceLabel}): {Source}", argsParsed.SourceLabel, argsParsed.SourceConnectionString);
            Log.Logger.Information("📊 Target ({TargetLabel}): {Target}", argsParsed.TargetLabel, argsParsed.TargetConnectionString);

            var comparisonResults = await comparer.CompareAsync(
                sourceProvider, targetProvider,
                unifiedLogger, argsParsed.TestMode, argsParsed.TestObjectLimit,
                comparisonOptions);

            var resultList = comparisonResults.ToList();

            int matchCount         = resultList.Count(r => r.Status == ComparisonStatus.Match);
            int mismatchCount      = resultList.Count(r => r.Status == ComparisonStatus.Mismatch);
            int missingInTargetCount = resultList.Count(r => r.Status == ComparisonStatus.MissingInTarget);
            int missingInSourceCount = resultList.Count(r => r.Status == ComparisonStatus.MissingInSource);

            Log.Logger.Information("📋 Compared {Total} schema objects", resultList.Count);
            Log.Logger.Information("  ✅ Match:                   {Match}",         matchCount);
            Log.Logger.Information("  ⚠️  Mismatch:                {Mismatch}",      mismatchCount);
            Log.Logger.Information("  ❌ Missing in {TargetLabel}: {MissingTarget}", argsParsed.TargetLabel, missingInTargetCount);
            Log.Logger.Information("  ❌ Missing in {SourceLabel}: {MissingSource}", argsParsed.SourceLabel, missingInSourceCount);

            var byType = resultList
                .GroupBy(r => r.ObjectType)
                .Select(g => $"{g.Key}={g.Count()}(diff={g.Count(x => x.Status != ComparisonStatus.Match)})");
            Log.Logger.Information("  By type: {Types}", string.Join(", ", byType));

            var report = new SchemaComparisonReport
            {
                Title       = argsParsed.Title,
                SourceLabel = argsParsed.SourceLabel,
                TargetLabel = argsParsed.TargetLabel,
                GeneratedOn = DateTime.UtcNow,
                Results     = resultList
            };
            await HtmlReportGenerator.GenerateWithRazorAsync(report, argsParsed.OutputFile);
            Log.Logger.Information("✅ Report written to: {OutputFile}", argsParsed.OutputFile);

            if (argsParsed.GenerateSync)
            {
                var syncOptions = new SyncScriptOptions
                {
                    SourceLabel       = argsParsed.SourceLabel,
                    TargetLabel       = argsParsed.TargetLabel,
                    IncludeRiskyChanges = argsParsed.AllowRisky,
                    AllowDestructive  = argsParsed.AllowDestructive,
                };
                var syncScript = new SyncScriptGenerator().Generate(resultList, syncOptions);
                var syncText   = new SyncScriptGenerator().Render(syncScript, syncOptions);
                await File.WriteAllTextAsync(argsParsed.SyncOutputFile, syncText);
                Log.Logger.Information("✅ Sync script written to: {SyncFile} ({Safe} safe, {Risky} risky, {Destructive} destructive statements)",
                    argsParsed.SyncOutputFile, syncScript.Safe.Count, syncScript.Risky.Count, syncScript.Destructive.Count);
            }

            // ── Exit code ─────────────────────────────────────────────────────
            if (!argsParsed.FailOnDrift)
                return 0;

            bool hasMissingInTarget = missingInTargetCount > 0;
            bool hasDrift           = mismatchCount > 0 || missingInSourceCount > 0 || hasMissingInTarget;

            if (!hasDrift)
                return 0;

            // Exit 2 = objects missing in target (more severe — prod is behind)
            if (hasMissingInTarget)
            {
                Log.Logger.Warning("🚨 DRIFT DETECTED — {Count} object(s) missing in {Target}. Exiting with code 2.",
                    missingInTargetCount, argsParsed.TargetLabel);
                return 2;
            }

            // Exit 1 = mismatches / objects missing only in source
            Log.Logger.Warning("⚠️  DRIFT DETECTED — {Mismatch} mismatch(es), {MissingSource} missing in source. Exiting with code 1.",
                mismatchCount, missingInSourceCount);
            return 1;
        }
        catch (Exception ex)
        {
            Log.Logger.Fatal(ex, "❌ Error during operation");
            return 3;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}