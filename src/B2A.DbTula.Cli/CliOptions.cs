namespace B2A.DbTula.Cli;

public class CliOptions
{
    public string SourceConnectionString { get; set; }
    public string TargetConnectionString { get; set; }
    public DbType SourceType { get; set; }
    public DbType TargetType { get; set; }
    public string OutputFile { get; set; } = "schema-sync.html";

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
                case "--sourceType":
                    if (i + 1 < args.Length && Enum.TryParse<DbType>(args[++i], true, out var srcType))
                        options.SourceType = srcType;
                    break;
                case "--targetType":
                    if (i + 1 < args.Length && Enum.TryParse<DbType>(args[++i], true, out var tgtType))
                        options.TargetType = tgtType;
                    break;
                case "--out":
                    if (i + 1 < args.Length) options.OutputFile = args[++i];
                    break;
            }
        }

        return options;
    }

    public string GetUsage()
    {
        return @"
                Usage:
                  dotnet db-tula.cli.dll --source <src-conn> --target <tgt-conn> --sourceType postgres --targetType mysql [--out schema-sync.html]

                Supported Types: postgres, mysql
                ";
    }
}