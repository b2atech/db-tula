namespace B2A.DbTula.Api.Data;

public enum DbEnvironment { QA, UAT, Prod, Other }
public enum DbKind { Postgres, MySql }

public class RegisteredDatabase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DbKind DbType { get; set; }
    public DbEnvironment Environment { get; set; }

    // AES-256 encrypted via ASP.NET Core Data Protection
    public string ConnectionStringEncrypted { get; set; } = "";

    // If true, this credential is used for sync apply (write account)
    public bool IsWriteAccount { get; set; }

    // Write account references its paired read account
    public Guid? ReadAccountId { get; set; }

    public Guid CreatedById { get; set; }
    public AppUser CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
