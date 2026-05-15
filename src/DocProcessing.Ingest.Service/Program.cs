using DocProcessing.Common.Observability;
using DocProcessing.Ingest.Service;
using MassTransit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOptions<IngestOptions>()
    .Bind(builder.Configuration.GetSection(IngestOptions.SectionName));

// Aspire-injected blob client (connection name "blobs" matches AppHost.cs)
builder.AddAzureBlobServiceClient("blobs");

builder.Services.AddSingleton<ITifSplitter, TifSplitter>();
builder.Services.AddSingleton<IBlobUploader, BlobUploader>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<BatchArrivedConsumer>();
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.UseDocProcDocumentIdLogScope(ctx);
        cfg.Host(builder.Configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Connection string 'messaging' not found"));
        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
