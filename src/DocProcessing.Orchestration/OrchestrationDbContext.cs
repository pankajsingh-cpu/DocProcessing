using Microsoft.EntityFrameworkCore;

namespace DocProcessing.Orchestration;

public class OrchestrationDbContext(DbContextOptions<OrchestrationDbContext> options) : DbContext(options)
{
    public DbSet<DocumentSagaState> DocumentSagas => Set<DocumentSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentSagaState>(entity =>
        {
            entity.HasKey(x => x.CorrelationId);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.Property(x => x.CurrentState).HasMaxLength(64).IsRequired();
            entity.Property(x => x.BatchId).HasMaxLength(256);
            entity.Property(x => x.SourceBlobPath).HasMaxLength(1024);
            entity.Property(x => x.OcrBlobPath).HasMaxLength(1024);
            entity.Property(x => x.FailureStage).HasMaxLength(64);
            entity.Property(x => x.FailureError).HasMaxLength(2048);
        });
    }
}

// Used by `dotnet ef migrations add` at design time when the AppHost isn't running.
public class OrchestrationDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<OrchestrationDbContext>
{
    public OrchestrationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrchestrationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=docproc-design;Trusted_Connection=True;")
            .Options;
        return new OrchestrationDbContext(options);
    }
}
