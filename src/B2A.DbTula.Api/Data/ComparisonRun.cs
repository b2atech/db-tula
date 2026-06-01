namespace B2A.DbTula.Api.Data;

public enum RunStatus { Pending, Running, Completed, Failed }

public class ComparisonRun
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ProfileId { get; set; }
    public ComparisonProfile? Profile { get; set; }
    public Guid? BatchRunId { get; set; }

    public Guid SourceDbId { get; set; }
    public RegisteredDatabase SourceDb { get; set; } = null!;

    public Guid TargetDbId { get; set; }
    public RegisteredDatabase TargetDb { get; set; } = null!;

    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public Guid InitiatedById { get; set; }
    public AppUser InitiatedBy { get; set; } = null!;

    // Serialized List<ComparisonResult>
    public string? ResultJson { get; set; }

    public string? SyncScriptSafe { get; set; }
    public string? SyncScriptRisky { get; set; }
    public string? SyncScriptDestructive { get; set; }

    // { match, mismatch, missingInTarget, missingInSource }
    public string? SummaryJson { get; set; }

    public string? ErrorMessage { get; set; }

    public ICollection<SyncApplyLog> ApplyLogs { get; set; } = [];
}
