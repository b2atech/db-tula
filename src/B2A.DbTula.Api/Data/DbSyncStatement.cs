namespace B2A.DbTula.Api.Data;

public class DbSyncStatement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ComparisonRunId { get; set; }
    public ComparisonRun ComparisonRun { get; set; } = null!;
    public string Category { get; set; } = "";   // Safe | Risky | Destructive
    public string ObjectType { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string Sql { get; set; } = "";
    public string Comment { get; set; } = "";
    public int OrderIndex { get; set; }
    public bool IsApproved { get; set; } = true;
    public bool IsApplied { get; set; }
    public DateTime? AppliedAt { get; set; }
    public Guid? AppliedById { get; set; }
    public AppUser? AppliedBy { get; set; }
}
