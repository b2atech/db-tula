namespace B2A.DbTula.Api.Data;

public class DriftMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComparisonRunId { get; set; }
    public ComparisonRun ComparisonRun { get; set; } = null!;
    public DateTime RunDate { get; set; } = DateTime.UtcNow;
    public string ObjectType { get; set; } = "";
    public int MatchCount { get; set; }
    public int MismatchCount { get; set; }
    public int MissingInTargetCount { get; set; }
    public int MissingInSourceCount { get; set; }
}
