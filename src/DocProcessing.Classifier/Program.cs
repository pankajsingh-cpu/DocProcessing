using DocProcessing.Classifier;
using DocProcessing.Common.Llm;
using DocProcessing.Common.Observability;
using DocProcessing.Embeddings;
using MassTransit;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(sp)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOptions<ClassifierOptions>()
    .Bind(builder.Configuration.GetSection(ClassifierOptions.SectionName));

builder.AddAzureBlobServiceClient("blobs");

// Each worker is its own process, so the Classifier hosts its own seeded KB
// (in-memory today; Azure AI Search in cloud — Prompt 12+).
builder.Services.AddIntentKnowledgeBase(builder.Configuration);

builder.Services.AddDocProcChatClient(builder.Configuration);

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var chat = sp.GetRequiredService<IChatClient>();
    var kb = sp.GetRequiredService<IIntentKnowledgeBase>();
    return ClassifierAgentFactory.Create(chat, kb);
});

builder.Services.AddSingleton<IIntentClassifier, IntentClassifier>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ClassifyConsumer>();
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
