using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DocProcessing.Persistence.Sql;

// Applies pending Persistence migrations on startup so the
// ClassificationRecord table exists before the saga first tries to write.
public sealed class PersistenceMigratorHostedService(
    IServiceProvider sp,
    ILogger<PersistenceMigratorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersistenceDbContext>();

        logger.LogInformation("Applying persistence DB migrations");
        await db.Database.MigrateAsync(stoppingToken);
        logger.LogInformation("Persistence DB migrations applied");
    }
}
