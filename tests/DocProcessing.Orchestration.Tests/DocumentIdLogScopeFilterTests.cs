using DocProcessing.Common.Observability;
using DocProcessing.Contracts.Events;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Events;

namespace DocProcessing.Orchestration.Tests;

// Drives the saga via the MassTransit test harness with the filter registered
// in the consume pipeline, and asserts that log entries emitted while the
// saga consumes a message carry the {DocumentId} property.
public sealed class DocumentIdLogScopeFilterTests
{
    private sealed class CapturingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class LoggingPersistence : DocProcessing.Common.Persistence.IPersistenceService
    {
        public Task SaveClassificationAsync(ClassificationCompletedEvent evt, CancellationToken ct)
        {
            // This log goes through Serilog because Log.Logger is configured below.
            Log.Information("saving classification");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeArchive : DocProcessing.Common.Persistence.IArchiveService
    {
        public Task ArchiveAsync(Guid documentId, string sourceBlobPath, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task DocumentId_Property_Is_Enriched_During_Saga_Consume()
    {
        var sink = new CapturingSink();
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            await using var provider = new ServiceCollection()
                .AddSingleton(NullLoggerFactory.Instance)
                .AddLogging()
                .AddSingleton<DocProcessing.Common.Persistence.IPersistenceService, LoggingPersistence>()
                .AddSingleton<DocProcessing.Common.Persistence.IArchiveService, FakeArchive>()
                .AddMassTransitTestHarness(x =>
                {
                    x.AddSagaStateMachine<DocumentSaga, DocumentSagaState>().InMemoryRepository();

                    x.UsingInMemory((ctx, cfg) =>
                    {
                        cfg.UseDocProcDocumentIdLogScope(ctx);
                        cfg.ConfigureEndpoints(ctx);
                    });
                })
                .BuildServiceProvider(true);

            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            var docId = Guid.NewGuid();
            var sagaHarness = harness.GetSagaStateMachineHarness<DocumentSaga, DocumentSagaState>();

            await harness.Bus.Publish(new DocumentIngestedEvent(
                docId, "batch-1", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));
            (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();

            await harness.Bus.Publish(new OcrCompletedEvent(docId, "ocr/x.json", 1, DateTimeOffset.UtcNow));
            (await sagaHarness.Consumed.Any<OcrCompletedEvent>()).Should().BeTrue();

            await harness.Bus.Publish(new ClassificationCompletedEvent(
                docId,
                [new IntentResult(
                    IntentName: "drip_ocp",
                    Confidence: 0.9,
                    Payload: System.Text.Json.JsonDocument.Parse("""{"transaction_type":"drip_ocp"}""").RootElement)],
                DateTimeOffset.UtcNow));
            (await sagaHarness.Consumed.Any<ClassificationCompletedEvent>()).Should().BeTrue();

            await harness.Stop();

            // LoggingPersistence emits "saving classification" while the saga
            // is consuming ClassificationCompletedEvent (a DocumentId-bearing
            // message), so that log line must carry {DocumentId}.
            var enriched = sink.Events.Where(e =>
                e.Properties.TryGetValue("DocumentId", out var p) &&
                p.ToString().Contains(docId.ToString())).ToList();
            enriched.Should().NotBeEmpty(
                "the filter must push DocumentId into LogContext for every consume");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
