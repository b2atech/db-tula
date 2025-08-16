using B2A.DbTula.Cli;
using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Cli.Helpers;
using B2A.DbTula.Cli.Reports;
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

            if (argsParsed.IsExtract)
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
                    IgnoreOwnership = argsParsed.IgnoreOwnership
                };

                var comparisonResults = await comparer.CompareAsync(
                    sourceProvider,
                    targetProvider,
                    unifiedLogger,
                    argsParsed.TestMode,
                    argsParsed.TestObjectLimit,
                    comparisonOptions);

                var report = new SchemaComparisonReport
                {
                    Title = argsParsed.Title,
                    GeneratedOn = DateTime.UtcNow,
                    Results = comparisonResults.ToList()
                };
                await HtmlReportGenerator.GenerateWithRazorAsync(report, argsParsed.OutputFile);

                Log.Logger.Information("✅ Comparison and report generation completed.");
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