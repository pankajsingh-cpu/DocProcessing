using DocProcessing.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DocProcessing.Persistence.Sql;

public static class PersistenceServiceCollectionExtensions
{
    // Wires PersistenceDbContext (Aspire-bound to the named connection string),
    // IPersistenceService, and the startup migrator. Connection name defaults
    // to "docproc" — same SQL database as the saga, separate migrations table.
    public static IHostApplicationBuilder AddDocProcSqlPersistence(
        this IHostApplicationBuilder builder,
        string connectionName = "docproc")
    {
        builder.AddSqlServerDbContext<PersistenceDbContext>(
            connectionName,
            configureDbContextOptions: opts =>
            {
                opts.UseSqlServer(sql =>
                    sql.MigrationsHistoryTable(PersistenceDbContext.MigrationsHistoryTable));
            });

        builder.Services.AddScoped<IPersistenceService, PersistenceService>();
        builder.Services.AddHostedService<PersistenceMigratorHostedService>();

        return builder;
    }
}
