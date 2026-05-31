namespace B2A.DbTula.Api.Data;

public class SyncApplyLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ComparisonRunId { get; set; }
    public ComparisonRun ComparisonRun { get; set; } = null!;

    public Guid AppliedById { get; set; }
    public AppUser AppliedBy { get; set; } = null!;

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    public Guid TargetDbId { get; set; }
    public RegisteredDatabase TargetDb { get; set; } = null!;

    public string SqlExecuted { get; set; } = "";
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string? ErrorDetails { get; set; }
}
