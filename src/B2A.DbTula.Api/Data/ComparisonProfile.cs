namespace B2A.DbTula.Api.Data;

public class ComparisonProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    public Guid SourceDbId { get; set; }
    public RegisteredDatabase SourceDb { get; set; } = null!;

    public Guid TargetDbId { get; set; }
    public RegisteredDatabase TargetDb { get; set; } = null!;

    public bool IgnoreOwnership { get; set; } = true;

    public Guid CreatedById { get; set; }
    public AppUser CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ComparisonRun> Runs { get; set; } = [];
}
