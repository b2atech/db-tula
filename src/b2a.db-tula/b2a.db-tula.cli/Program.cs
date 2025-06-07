using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using b2a.db_tula;
using b2a.db_tula.cli;
using b2a.db_tula.core;
using b2a.db_tula.core.Models;

namespace b2a.db_tula.runner
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            LogLevel logLevel = LogLevel.Basic;

            if (args.Contains("--verbose"))
            {
                logLevel = LogLevel.Verbose;
            }

            var options = CliOptions.Parse(args);
            if (!options.IsValid)
            {
                Console.WriteLine(options.GetUsage());
                return 1;
            }

            try
            {
                var sourceConnection = new DatabaseConnection(options.SourceConnectionString);
                var targetConnection = new DatabaseConnection(options.TargetConnectionString);

                var schemaFetcherSrc = new SchemaFetcher(sourceConnection, Console.WriteLine, logLevel);
                var schemaFetcherTgt = new SchemaFetcher(targetConnection, Console.WriteLine, logLevel);
                var syncer = new SchemaSyncer(sourceConnection, targetConnection, Console.WriteLine);
                var comparisonRunner = new SchemaComparisonRunner(schemaFetcherSrc, schemaFetcherTgt,syncer, logLevel);
                var report = await comparisonRunner.RunComparisonAsync((msg, level) => {
                    if (level <= LogLevel.Basic) Console.WriteLine(msg);
                });
                //var report = HtmlReportGenerator.LoadFromJson("schema-comparison.json");
                HtmlReportGenerator.SaveAsJson(report, Path.ChangeExtension(options.OutputFile, ".json"));
                await HtmlReportGenerator.GenerateWithRazorAsync(report.AllResults(), options.OutputFile);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during sync generation: {ex.Message}");
                return 1;
            }
        }
    }
}
