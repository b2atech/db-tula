using B2A.DbTula.Cli;
using B2A.DbTula.Cli.Factories;
using B2A.DbTula.Cli.Helpers;
using B2A.DbTula.Cli.Reports;
using B2A.DbTula.Core.Enums;
using Serilog;

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


            var comparisonResults = await comparer.CompareAsync(sourceProvider, targetProvider, unifiedLogger, argsParsed.TestMode, argsParsed.TestObjectLimit);

            var report = new SchemaComparisonReport
            {
                Title = argsParsed.Title, // optional, or customize
                GeneratedOn = DateTime.UtcNow,
                Results = comparisonResults.ToList()
            };
            await HtmlReportGenerator.GenerateWithRazorAsync(report, argsParsed.OutputFile);

            Log.Logger.Information("✅ Comparison and report generation completed.");
        }
        catch (Exception ex)
        {
            Log.Logger.Fatal(ex, "❌ Error during schema comparison");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}