namespace B2A.DbTula.Cli;

public class CliOptions
{
    public string SourceConnectionString { get; set; }
    public string TargetConnectionString { get; set; }
    public DbType SourceType { get; set; }
    public DbType TargetType { get; set; }
    public string OutputFile { get; set; } = "schema-sync.html";

    // New test options
    public bool TestMode { get; set; } = false;
    public int TestObjectLimit { get; set; } = 10;

    // New title option
    public string Title { get; set; } = "Schema Comparison Report";

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SourceConnectionString) &&
        !string.IsNullOrWhiteSpace(TargetConnectionString);

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--source":
                    if (i + 1 < args.Length) options.SourceConnectionString = args[++i];
                    break;
                case "--target":
                    if (i + 1 < args.Length) options.TargetConnectionString = args[++i];
                    break;
                case "--sourcetype":
                    if (i + 1 < args.Length && Enum.TryParse<DbType>(args[++i], true, out var srcType))
                        options.SourceType = srcType;
                    break;
                case "--targettype":
                    if (i + 1 < args.Length && Enum.TryParse<DbType>(args[++i], true, out var tgtType))
                        options.TargetType = tgtType;
                    break;
                case "--out":
                    if (i + 1 < args.Length) options.OutputFile = args[++i];
                    break;
                case "--test":
                    options.TestMode = true;
                    break;
                case "--limit":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var limit))
                        options.TestObjectLimit = limit;
                    break;
                case "--title":
                    if (i + 1 < args.Length) options.Title = args[++i];
                    break;
            }
        }

        return options;
    }

    public string GetUsage()
    {
        return @"
            Usage:
              dotnet db-tula.cli.dll --source <src-conn> --target <tgt-conn> --sourceType postgres --targetType mysql [--out schema-sync.html] [--test] [--limit 5] [--title ""My Report""]

            Supported Types:
              postgres, mysql

            Options:
              --test               Enable test mode (only compare limited number of objects)
              --limit <number>     Number of objects to compare when in test mode (default: 10)
              --title <text>       Custom title to be shown in the HTML report header
            ";
    }
}
