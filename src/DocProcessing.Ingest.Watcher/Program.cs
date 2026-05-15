using DocProcessing.Ingest.Watcher;
using MassTransit;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOptions<WatcherOptions>()
    .Bind(builder.Configuration.GetSection(WatcherOptions.SectionName))
    .PostConfigure(opts =>
    {
        // LANDING_PATH env var (set by AppHost) overrides config when present.
        var envPath = builder.Configuration["LANDING_PATH"];
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            opts.LandingPath = envPath;
        }

        if (string.IsNullOrWhiteSpace(opts.LandingPath))
        {
            throw new InvalidOperationException(
                "LANDING_PATH (or Watcher:LandingPath) must be set.");
        }
    });

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Connection string 'messaging' not found"));
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddHealthChecks()
    .AddCheck<LandingFolderHealthCheck>("landing_folder");

builder.Services.AddHostedService<LandingFolderWatcher>();

var host = builder.Build();
host.Run();
