using B2A.DbTula.Core.Models;

namespace B2A.DbTula.Cli.Reports;
public class SchemaComparisonReport
{
    public string Title { get; set; } = "Schema Comparison Report";
    public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
    public List<ComparisonResult> Results { get; set; } = new();
}