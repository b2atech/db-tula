namespace B2A.DbTula.Cli;

public class CliOptions
{
    // Comparison options
    public string SourceConnectionString { get; set; }
    public string TargetConnectionString { get; set; }
    public DbType SourceType { get; set; }
    public DbType TargetType { get; set; }
    public string OutputFile { get; set; } = "schema-sync.html";
    public bool TestMode { get; set; } = false;
    public int TestObjectLimit { get; set; }
    public string Title { get; set; } = "Schema Comparison Report";
    public bool IgnoreOwnership { get; set; } = true;

    // Extraction options
    public bool ExtractMode { get; set; } = false;
    public string ExtractConnectionString { get; set; }
    public DbType ExtractType { get; set; }
    public string OutputDir { get; set; } = "dbobjects";
    public string ExtractObjects { get; set; } = "all"; // e.g. views,functions,tables
    public bool OverwriteFiles { get; set; } = false;

    // Batch configuration file
    public string BatchConfigFile { get; set; }
    public bool IsBatchMode { get; set; } = false;

    // Helper: Which operation?
    public bool IsCompare => !ExtractMode && !IsBatchMode;
    public bool IsExtract => ExtractMode && !IsBatchMode;

    public bool IsValid =>
        (IsCompare && !string.IsNullOrWhiteSpace(SourceConnectionString) && !string.IsNullOrWhiteSpace(TargetConnectionString))
        || (IsExtract && !string.IsNullOrWhiteSpace(ExtractConnectionString))
        || (IsBatchMode && !string.IsNullOrWhiteSpace(BatchConfigFile));

    public IEnumerable<string> ExtractObjectTypes =>
        ExtractObjects == "all"
        ? new[] { "functions", "procedures", "views", "triggers", "tables" }
            : ExtractObjects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                // --- Comparison options ---
                case "--source":
                    if (i + 1 < args.Length) options.SourceConnectionString = args[++i];
                    break;
                case "--target":
                    if (i + 1 < args.Length) options.TargetConnectionString = args[++i];
                    break;
                case "--sourcetype":
                case "--source-type":
                    if (i + 1 < args.Length && Enum.TryParse<DbType>(args[++i], true, out var srcType))
                        options.SourceType = srcType;
                    break;
                case "--targettype":
                case "--target-type":
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
                case "--ignore-ownership":
                    options.IgnoreOwnership = true;
                    break;
                case "--no-ignore-ownership":
                    options.IgnoreOwnership = false;
                    break;

                // --- Extraction options ---
                case "extract": // verb style
                case "--extract":
                    options.ExtractMode = true;
                    break;
                case "--extract-conn":
                case "--extractconnection":
                    if (i + 1 < args.Length) options.ExtractConnectionString = args[++i];
                    break;
                case "--extract-type":
                case "--extracttype":
                    if (i + 1 < args.Length && Enum.TryParse<DbType>(args[++i], true, out var extType))
                        options.ExtractType = extType;
                    break;
                case "--outputdir":
                    if (i + 1 < args.Length) options.OutputDir = args[++i];
                    break;
                case "--objects":
                case "--extractobjects":
                    if (i + 1 < args.Length) options.ExtractObjects = args[++i];
                    break;
                case "--overwrite":
                    options.OverwriteFiles = true;
                    break;

                // --- Batch mode options ---
                case "--batch":
                case "--config":
                    if (i + 1 < args.Length)
                    {
                        options.BatchConfigFile = args[++i];
                        options.IsBatchMode = true;
                    }
                    break;
            }
        }

        return options;
    }

    public string GetUsage()
    {
        return @"
Usage:
  dotnet db-tula.cli.dll compare --source <src-conn> --target <tgt-conn> --sourceType postgres --targetType mysql [--out schema-sync.html] [--test] [--limit 5] [--title ""My Report""]
  dotnet db-tula.cli.dll extract --extract-conn <conn> --extract-type postgres --outputDir dbobjects [--objects views,functions] [--overwrite]
  dotnet db-tula.cli.dll --batch <config-file.json>

Supported Types:
  postgres, mysql

Options:
  --test               Enable test mode (only compare limited number of objects)
  --limit <number>     Number of objects to compare when in test mode (default: 10)
  --title <text>       Custom title to be shown in the HTML report header
  --ignore-ownership   Ignore owner/definer differences in schema objects (default: true)
  --no-ignore-ownership  Do not ignore owner/definer differences

  --extract            Extraction mode (can also use verb 'extract')
  --extract-conn       Connection string for extraction
  --extract-type       Database type for extraction (postgres, mysql)
  --outputDir          Directory to write extracted .sql files (default: dbobjects)
  --objects            Object types to extract (e.g. views,functions,tables; default: all)
  --overwrite          Overwrite existing files

  --batch <file>       Run multiple extractions/comparisons from a JSON configuration file
  --config <file>      Alias for --batch

Batch Configuration File Example:
{
  ""extractionJobs"": [
    {
      ""name"": ""DB1"",
      ""connectionString"": ""Server=...; Database=db1; ...,
      ""dbType"": ""postgres"",
      ""outputDir"": ""./extracted-db1"",
      ""objects"": ""functions,procedures,views,triggers"",
      ""overwrite"": true
    },
    {
      ""name"": ""DB2"",
      ""connectionString"": ""Server=...; Database=db2; ...,
      ""dbType"": ""postgres"",
      ""outputDir"": ""./extracted-db2"",
      ""objects"": ""all"",
      ""overwrite"": true
    }
  ],
  ""comparisonJobs"": [
    {
      ""name"": ""Compare DB1 to DB2"",
      ""sourceConnectionString"": ""Server=...; Database=db1; ...,
      ""targetConnectionString"": ""Server=...; Database=db2; ...,
      ""sourceType"": ""postgres"",
      ""targetType"": ""postgres"",
      ""outputFile"": ""./comparison-db1-db2.html"",
      ""title"": ""DB1 vs DB2 Schema Comparison"",
      ""ignoreOwnership"": true
    }
  ]
}
";
    }
}