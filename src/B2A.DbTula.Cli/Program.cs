using B2a.DbTula.Infrastructure.Postgres;
using B2A.DbTula.Cli;
using B2A.DbTula.Core.Enums;
using Serilog;

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

    var comparer = new SchemaComparer();

    var sourceProvider = new PostgresSchemaProvider(argsParsed.SourceConnectionString,Console.WriteLine,verbose: true,logLevel: LogLevel.Basic);
    var targetProvider = new PostgresSchemaProvider(argsParsed.TargetConnectionString, Console.WriteLine, verbose: true, logLevel: LogLevel.Basic);

    var output =     await comparer.CompareAsync(sourceProvider,targetProvider, (i, total, tableName) => Console.WriteLine($"Tables compared: {i}/{total} - {tableName}"), true);
    
    Log.Logger.Information("✅ Comparison and reprt generation completed.");
}
catch (Exception ex)
{
    Log.Logger.Fatal(ex, "❌ Error during schema comparison");
}
finally
{
    Log.CloseAndFlush();
}
