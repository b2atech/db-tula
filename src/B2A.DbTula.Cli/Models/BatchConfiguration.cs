namespace B2A.DbTula.Cli.Models;

public class BatchConfiguration
{
    public List<ExtractionJob>? ExtractionJobs { get; set; }
    public List<ComparisonJob>? ComparisonJobs { get; set; }
}

public class ExtractionJob
{
    public string Name { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string DbType { get; set; } = "postgres";
    public string OutputDir { get; set; } = "dbobjects";
    public string Objects { get; set; } = "all";
    public bool Overwrite { get; set; } = false;
}

public class ComparisonJob
{
    public string Name { get; set; } = string.Empty;
    public string SourceConnectionString { get; set; } = string.Empty;
    public string TargetConnectionString { get; set; } = string.Empty;
    public string SourceType { get; set; } = "postgres";
    public string TargetType { get; set; } = "postgres";
    public string OutputFile { get; set; } = "schema-sync.html";
    public string Title { get; set; } = "Schema Comparison Report";
    public bool IgnoreOwnership { get; set; } = true;
}
