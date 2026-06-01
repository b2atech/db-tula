namespace B2A.DbTula.Api.Data;

public class BatchRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int TotalRuns { get; set; }
    public int CompletedRuns { get; set; }
    public int FailedRuns { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public Guid InitiatedById { get; set; }
    public AppUser InitiatedBy { get; set; } = null!;
}
