using Azure;
using Azure.AI.DocumentIntelligence;
using DocProcessing.Common.Observability;
using DocProcessing.Ocr;
using MassTransit;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOptions<OcrOptions>()
    .Bind(builder.Configuration.GetSection(OcrOptions.SectionName))
    .PostConfigure(opts =>
    {
        // OCR_MODE env var override (set by AppHost).
        var envMode = builder.Configuration["OCR_MODE"];
        if (!string.IsNullOrWhiteSpace(envMode)) opts.Mode = envMode;
    });

builder.Services.AddOptions<DocumentIntelligenceOptions>()
    .Bind(builder.Configuration.GetSection(DocumentIntelligenceOptions.SectionName));

builder.AddAzureBlobServiceClient("blobs");

// Engine selection — switch on OcrOptions.Mode resolved at startup.
var ocrSection = builder.Configuration.GetSection(OcrOptions.SectionName);
var resolvedMode =
    builder.Configuration["OCR_MODE"]
    ?? ocrSection["Mode"]
    ?? "real";

if (resolvedMode.Equals("stub", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IOcrEngine, StubOcrEngine>();
}
else
{
    var diSection = builder.Configuration.GetSection(DocumentIntelligenceOptions.SectionName);
    var endpoint = diSection["Endpoint"]
        ?? throw new InvalidOperationException(
            "AzureAI:DocumentIntelligence:Endpoint not set. Set OCR_MODE=stub for local dev.");
    var key = diSection["Key"]
        ?? throw new InvalidOperationException("AzureAI:DocumentIntelligence:Key not set.");

    builder.Services.AddSingleton(_ =>
        new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key)));
    builder.Services.AddSingleton<IOcrEngine, DocumentIntelligenceOcrEngine>();
}

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<RunOcrConsumer>();
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
