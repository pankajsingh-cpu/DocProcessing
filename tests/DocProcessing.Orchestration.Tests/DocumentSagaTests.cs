using DocProcessing.Common.Persistence;
using DocProcessing.Contracts.Commands;
using DocProcessing.Contracts.Events;
using DocProcessing.Orchestration;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocProcessing.Orchestration.Tests;

public class DocumentSagaTests
{
    private sealed class RecordingPersistence : IPersistenceService
    {
        public List<ClassificationCompletedEvent> Saved { get; } = new();
        public Task SaveClassificationAsync(ClassificationCompletedEvent evt, CancellationToken ct)
        {
            Saved.Add(evt);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingArchive : IArchiveService
    {
        public List<Guid> Archived { get; } = new();
        public Task ArchiveAsync(Guid documentId, CancellationToken ct)
        {
            Archived.Add(documentId);
            return Task.CompletedTask;
        }
    }

    private static async Task<(ITestHarness Harness, ISagaStateMachineTestHarness<DocumentSaga, DocumentSagaState> Saga, RecordingPersistence Persistence, RecordingArchive Archive, ServiceProvider Provider)> StartHarness()
    {
        var persistence = new RecordingPersistence();
        var archive = new RecordingArchive();

        var provider = new ServiceCollection()
            .AddSingleton<NullLoggerFactory>(NullLoggerFactory.Instance)
            .AddLogging()
            .AddSingleton<IPersistenceService>(persistence)
            .AddSingleton<IArchiveService>(archive)
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<DocumentSaga, DocumentSagaState>()
                    .InMemoryRepository();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var sagaHarness = harness.GetSagaStateMachineHarness<DocumentSaga, DocumentSagaState>();
        return (harness, sagaHarness, persistence, archive, provider);
    }

    [Fact]
    public async Task Ingested_Transitions_To_AwaitingOcr_And_Sends_RunOcrCommand()
    {
        var (harness, sagaHarness, _, _, provider) = await StartHarness();
        await using var _ = provider;

        var docId = Guid.NewGuid();
        await harness.Bus.Publish(new DocumentIngestedEvent(
            docId, "batch-1", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));

        (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();
        (await sagaHarness.Created.Any(s => s.CorrelationId == docId)).Should().BeTrue();

        var saga = sagaHarness.Created.ContainsInState(docId, sagaHarness.StateMachine, sagaHarness.StateMachine.AwaitingOcr);
        saga.Should().NotBeNull();

        (await harness.Sent.Any<RunOcrCommand>(c => c.Context.Message.DocumentId == docId)).Should().BeTrue();

        await harness.Stop();
    }

    [Fact]
    public async Task OcrCompleted_Transitions_From_AwaitingOcr_To_AwaitingClassification()
    {
        var (harness, sagaHarness, _, _, provider) = await StartHarness();
        await using var _ = provider;

        var docId = Guid.NewGuid();
        await harness.Bus.Publish(new DocumentIngestedEvent(
            docId, "batch-1", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();

        await harness.Bus.Publish(new OcrCompletedEvent(
            docId, "ocr/x.json", PageCount: 3, DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<OcrCompletedEvent>()).Should().BeTrue();

        var saga = sagaHarness.Created.ContainsInState(docId, sagaHarness.StateMachine, sagaHarness.StateMachine.AwaitingClassification);
        saga.Should().NotBeNull();
        saga!.OcrBlobPath.Should().Be("ocr/x.json");

        (await harness.Sent.Any<ClassifyCommand>(c => c.Context.Message.DocumentId == docId)).Should().BeTrue();

        await harness.Stop();
    }

    [Fact]
    public async Task ClassificationCompleted_Persists_Archives_And_Finalizes()
    {
        var (harness, sagaHarness, persistence, archive, provider) = await StartHarness();
        await using var _ = provider;

        var docId = Guid.NewGuid();

        await harness.Bus.Publish(new DocumentIngestedEvent(
            docId, "batch-1", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();

        await harness.Bus.Publish(new OcrCompletedEvent(
            docId, "ocr/x.json", PageCount: 1, DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<OcrCompletedEvent>()).Should().BeTrue();

        var classification = new ClassificationCompletedEvent(
            docId,
            Intents: [new IntentResult("change_of_address", [1], 0.95, new Dictionary<string, string?>())],
            CompletedAt: DateTimeOffset.UtcNow);

        await harness.Bus.Publish(classification);
        (await sagaHarness.Consumed.Any<ClassificationCompletedEvent>()).Should().BeTrue();

        persistence.Saved.Should().ContainSingle(e => e.DocumentId == docId);
        archive.Archived.Should().Contain(docId);

        // SetCompletedWhenFinalized removes the saga from the repo on Finalize().
        // ContainsInState(_, Persisted) should be null because instance is gone.
        sagaHarness.Created
            .ContainsInState(docId, sagaHarness.StateMachine, sagaHarness.StateMachine.Persisted)
            .Should().BeNull("after finalize the saga is removed");

        await harness.Stop();
    }

    [Fact]
    public async Task DocumentFailed_Transitions_To_Failed_And_Sends_To_Dlq()
    {
        var (harness, sagaHarness, _, _, provider) = await StartHarness();
        await using var _ = provider;

        var docId = Guid.NewGuid();
        await harness.Bus.Publish(new DocumentIngestedEvent(
            docId, "batch-1", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();

        await harness.Bus.Publish(new DocumentFailedEvent(
            docId, Stage: "ocr", Error: "boom", StackTrace: null));
        (await sagaHarness.Consumed.Any<DocumentFailedEvent>()).Should().BeTrue();

        var saga = sagaHarness.Created.ContainsInState(docId, sagaHarness.StateMachine, sagaHarness.StateMachine.Failed);
        saga.Should().NotBeNull();
        saga!.FailureStage.Should().Be("ocr");
        saga.FailureError.Should().Be("boom");

        (await harness.Sent.Any<DocumentFailedEvent>(c =>
            c.Context.Message.DocumentId == docId &&
            c.Context.DestinationAddress!.AbsolutePath.EndsWith("/dlq"))).Should().BeTrue();

        await harness.Stop();
    }
}
