using DocProcessing.Common.Observability;
using DocProcessing.Orchestration;
using DocProcessing.Persistence.Archive;
using DocProcessing.Persistence.Sql;
using MassTransit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// Aspire injects the "docproc" connection string from AppHost.cs.
builder.AddSqlServerDbContext<OrchestrationDbContext>("docproc");

// Persistence — separate DbContext sharing the same DB (different migrations table).
builder.AddDocProcSqlPersistence();

// Archive — needs the same BlobServiceClient the Ingest + OCR modules use.
builder.AddAzureBlobServiceClient("blobs");
builder.Services.AddDocProcArchive(builder.Configuration);

builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<DocumentSaga, DocumentSagaState>()
        .EntityFrameworkRepository(r =>
        {
            r.ConcurrencyMode = MassTransit.ConcurrencyMode.Optimistic;
            r.ExistingDbContext<OrchestrationDbContext>();
            r.UseSqlServer();
        });

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.UseDocProcDocumentIdLogScope(ctx);
        cfg.Host(builder.Configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Connection string 'messaging' not found"));
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddHostedService<DbMigratorHostedService>();

var host = builder.Build();
host.Run();
