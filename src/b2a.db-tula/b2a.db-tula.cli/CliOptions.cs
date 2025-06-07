using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace b2a.db_tula.cli
{
    public class CliOptions
    {
        public string SourceConnectionString { get; set; }
        public string TargetConnectionString { get; set; }
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
  dotnet db-tula.dll --source <source-connection> --target <target-connection> [--out schema-sync.sql]

Example:
  dotnet db-tula.cli.dll --source ""Host=localhost;Port=5432;Database=dev;User Id=postgres;Password=123"" \
                              --target ""Host=prodhost;Port=5432;Database=prod;User Id=postgres;Password=456"" \
                              --out ./output.sql
";
        }
    }
}
