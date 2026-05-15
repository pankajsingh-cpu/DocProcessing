var builder = DistributedApplication.CreateBuilder(args);

// LLM endpoints — GitHub Models for local dev, swapped to Azure OpenAI in prod.
// Consumed by Classifier (chat) and KnowledgeBase (embeddings).
const string LlmEndpoint    = "https://models.github.ai/inference";
const string ChatModel      = "openai/gpt-4.1";
const string EmbeddingModel = "openai/text-embedding-3-small";

var githubToken = builder.Configuration["GITHUB_TOKEN"]
    ?? throw new InvalidOperationException(
        "GITHUB_TOKEN is not set. Run: " +
        "dotnet user-secrets set GITHUB_TOKEN ghp_xxx --project src/DocProcessing.AppHost");

// --- Infra ---------------------------------------------------------------

var sqlServer = builder.AddSqlServer("sql");
var docproc   = sqlServer.AddDatabase("docproc");

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var blobs   = storage.AddBlobs("blobs");

var rabbit = builder.AddRabbitMQ("messaging");

// --- Modules -------------------------------------------------------------

var orchestrator = builder.AddProject<Projects.DocProcessing_Orchestration>("orchestrator")
    .WithReference(docproc).WaitFor(docproc)
    .WithReference(blobs).WaitFor(blobs)
    .WithReference(rabbit).WaitFor(rabbit);

var ingest = builder.AddProject<Projects.DocProcessing_Ingest_Service>("ingest")
    .WithReference(blobs).WaitFor(blobs)
    .WithReference(rabbit).WaitFor(rabbit);

var watcher = builder.AddProject<Projects.DocProcessing_Ingest_Watcher>("watcher")
    .WithReference(rabbit).WaitFor(rabbit)
    .WithEnvironment("LANDING_PATH", "../../_landing");

var ocr = builder.AddProject<Projects.DocProcessing_Ocr>("ocr")
    .WithReference(blobs).WaitFor(blobs)
    .WithReference(rabbit).WaitFor(rabbit)
    // Local dev: no Azure Document Intelligence resource — use stub fixtures.
    // Override OCR_MODE=real in user-secrets/env to call the real DI endpoint.
    .WithEnvironment("OCR_MODE", "stub");

var classifier = builder.AddProject<Projects.DocProcessing_Classifier>("classifier")
    .WithReference(blobs).WaitFor(blobs)
    .WithReference(rabbit).WaitFor(rabbit)
    .WithEnvironment("Llm__Endpoint", LlmEndpoint)
    .WithEnvironment("Llm__ChatModel", ChatModel)
    .WithEnvironment("GITHUB_TOKEN", githubToken);

var kb = builder.AddProject<Projects.DocProcessing_KnowledgeBase>("kb")
    .WithEnvironment("Llm__Endpoint", LlmEndpoint)
    .WithEnvironment("Llm__EmbeddingModel", EmbeddingModel)
    .WithEnvironment("GITHUB_TOKEN", githubToken);

builder.Build().Run();
