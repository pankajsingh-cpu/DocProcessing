using Microsoft.EntityFrameworkCore;

namespace DocProcessing.Orchestration;

// Applies pending EF migrations on startup so the saga table exists before any
// message is consumed. Runs once, then idles.
public sealed class DbMigratorHostedService(
    IServiceProvider sp,
    ILogger<DbMigratorHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestrationDbContext>();

        logger.LogInformation("Applying orchestration DB migrations");
        await db.Database.MigrateAsync(stoppingToken);
        logger.LogInformation("Orchestration DB migrations applied");
    }
}
