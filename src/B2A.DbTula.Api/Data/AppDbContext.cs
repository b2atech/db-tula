using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace B2A.DbTula.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RegisteredDatabase> Databases => Set<RegisteredDatabase>();
    public DbSet<ComparisonProfile> Profiles => Set<ComparisonProfile>();
    public DbSet<ComparisonRun> ComparisonRuns => Set<ComparisonRun>();
    public DbSet<SyncApplyLog> SyncApplyLogs => Set<SyncApplyLog>();
    public DbSet<DriftMetric> DriftMetrics => Set<DriftMetric>();
    public DbSet<DbSyncStatement> SyncStatements => Set<DbSyncStatement>();
    public DbSet<AllowedEmail> AllowedEmails => Set<AllowedEmail>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<BatchRun> BatchRuns => Set<BatchRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.GoogleId).IsUnique();
            e.Property(u => u.Role).HasConversion<string>();
        });

        b.Entity<RegisteredDatabase>(e =>
        {
            e.Property(d => d.DbType).HasConversion<string>();
            e.Property(d => d.Environment).HasConversion<string>();
            e.HasOne(d => d.CreatedBy).WithMany(u => u.Databases)
                .HasForeignKey(d => d.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ComparisonProfile>(e =>
        {
            e.HasOne(p => p.SourceDb).WithMany()
                .HasForeignKey(p => p.SourceDbId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.TargetDb).WithMany()
                .HasForeignKey(p => p.TargetDbId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.CreatedBy).WithMany()
                .HasForeignKey(p => p.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ComparisonRun>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
            e.HasOne(r => r.Profile).WithMany(p => p.Runs)
                .HasForeignKey(r => r.ProfileId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasOne(r => r.SourceDb).WithMany()
                .HasForeignKey(r => r.SourceDbId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.TargetDb).WithMany()
                .HasForeignKey(r => r.TargetDbId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.InitiatedBy).WithMany(u => u.Runs)
                .HasForeignKey(r => r.InitiatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(r => r.StartedAt);
            e.HasIndex(r => r.Status);
        });

        b.Entity<SyncApplyLog>(e =>
        {
            e.HasOne(l => l.ComparisonRun).WithMany(r => r.ApplyLogs)
                .HasForeignKey(l => l.ComparisonRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.AppliedBy).WithMany(u => u.ApplyLogs)
                .HasForeignKey(l => l.AppliedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.TargetDb).WithMany()
                .HasForeignKey(l => l.TargetDbId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<BatchRun>(e =>
        {
            e.HasOne(b => b.InitiatedBy).WithMany()
                .HasForeignKey(b => b.InitiatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(b => b.StartedAt);
        });

        b.Entity<AllowedEmail>(e =>
        {
            e.HasIndex(a => a.Email).IsUnique();
            e.HasOne(a => a.AddedBy).WithMany()
                .HasForeignKey(a => a.AddedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DriftMetric>(e =>
        {
            e.HasOne(m => m.ComparisonRun).WithMany()
                .HasForeignKey(m => m.ComparisonRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.ComparisonRunId, m.ObjectType });
            e.HasIndex(m => m.RunDate);
        });

        b.Entity<DbSyncStatement>(e =>
        {
            e.ToTable("SyncStatements");
            e.HasOne(s => s.ComparisonRun).WithMany()
                .HasForeignKey(s => s.ComparisonRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.AppliedBy).WithMany()
                .HasForeignKey(s => s.AppliedById).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
            e.HasIndex(s => new { s.ComparisonRunId, s.Category });
        });
    }
}
