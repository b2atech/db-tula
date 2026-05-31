namespace B2A.DbTula.Api.Data;

public enum UserRole { Viewer, Operator, Admin }

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string GoogleId { get; set; } = "";
    public string Name { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RegisteredDatabase> Databases { get; set; } = [];
    public ICollection<ComparisonRun> Runs { get; set; } = [];
    public ICollection<SyncApplyLog> ApplyLogs { get; set; } = [];
}
