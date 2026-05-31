namespace B2A.DbTula.Api.Data;

public class AllowedEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public Guid AddedById { get; set; }
    public AppUser AddedBy { get; set; } = null!;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
