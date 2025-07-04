using Serilog;
using Serilog.Formatting.Json;

namespace B2A.DbTula.Cli;
public static class LoggerConfigurator
{
    public static ILogger ConfigureLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(new JsonFormatter(), "logs/cli-log.json", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        return Log.Logger;
    }
}