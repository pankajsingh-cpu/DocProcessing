using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocProcessing.Persistence.Sql;

public sealed class PersistenceDbContext(DbContextOptions<PersistenceDbContext> options)
    : DbContext(options)
{
    // Separate migrations history table so this context can coexist with
    // OrchestrationDbContext in the same SQL database without their migration
    // metadata trampling each other (CLAUDE.md invariant: no shared DbContexts
    // across module boundaries).
    public const string MigrationsHistoryTable = "__EFMigrationsHistory_Persistence";

    public DbSet<ClassificationRecord> ClassificationRecords => Set<ClassificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClassificationRecord>(entity =>
        {
            entity.HasKey(x => x.DocumentId);
            entity.Property(x => x.TransactionType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Intents).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ExtractedFields).HasColumnType("nvarchar(max)").IsRequired();
            entity.HasIndex(x => x.TransactionType);
            entity.HasIndex(x => x.CreatedDate);
        });
    }
}

// Used by `dotnet ef migrations add` at design time when the AppHost isn't running.
public sealed class PersistenceDbContextFactory : IDesignTimeDbContextFactory<PersistenceDbContext>
{
    public PersistenceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PersistenceDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=docproc-design;Trusted_Connection=True;",
                sql => sql.MigrationsHistoryTable(PersistenceDbContext.MigrationsHistoryTable))
            .Options;
        return new PersistenceDbContext(options);
    }
}
