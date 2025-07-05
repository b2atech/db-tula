using Serilog;

namespace B2A.DbTula.Cli.Helpers;
public static class LoggerHelpers
{
    public static Action<int, int, string, bool> CreateUnifiedLogger()
    {
        return (current, total, message, isProgress) =>
        {
            var logLine = isProgress && total > 0
                ? $"[{current}/{total}] {message}"
                : message;

            Console.WriteLine(logLine);
            Log.Information(logLine);
        };
    }
}