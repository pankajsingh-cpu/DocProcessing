using System.Diagnostics;
using DocProcessing.Contracts.Events;
using DocProcessing.Orchestration;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocProcessing.Orchestration.Tests;

// Drives the saga end-to-end and listens to the "DocProcessing" ActivitySource
// to make sure the document.process activities fire on the right transitions
// with the right tags. Independent of the OTel exporter pipeline — we attach
// an ActivityListener so we don't need ServiceDefaults wired up.
public sealed class DocProcessingActivitiesTests
{
    private sealed class CapturingListener : IDisposable
    {
        public List<Activity> Stopped { get; } = new();

        private readonly ActivityListener _listener;

        public CapturingListener()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == DocProcessingActivities.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = a => Stopped.Add(a),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class FakePersistence : DocProcessing.Common.Persistence.IPersistenceService
    {
        public Task SaveClassificationAsync(ClassificationCompletedEvent evt, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeArchive : DocProcessing.Common.Persistence.IArchiveService
    {
        public Task ArchiveAsync(Guid documentId, string sourceBlobPath, CancellationToken ct) => Task.CompletedTask;
    }

    private static ServiceProvider BuildHarnessProvider() =>
        new ServiceCollection()
            .AddSingleton(NullLoggerFactory.Instance)
            .AddLogging()
            .AddSingleton<DocProcessing.Common.Persistence.IPersistenceService, FakePersistence>()
            .AddSingleton<DocProcessing.Common.Persistence.IArchiveService, FakeArchive>()
            .AddMassTransitTestHarness(x =>
            {
                x.AddSagaStateMachine<DocumentSaga, DocumentSagaState>().InMemoryRepository();
            })
            .BuildServiceProvider(true);

    [Fact]
    public async Task Ingested_Emits_DocumentProcess_Activity_With_Tags()
    {
        using var capture = new CapturingListener();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var docId = Guid.NewGuid();
        await harness.Bus.Publish(new DocumentIngestedEvent(
            docId, "batch-42", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));

        var sagaHarness = harness.GetSagaStateMachineHarness<DocumentSaga, DocumentSagaState>();
        (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();

        await harness.Stop();

        // Filter by docId — xUnit runs test classes concurrently and the
        // listener is process-global, so other tests' activities also flow
        // into capture.Stopped.
        var startActivity = capture.Stopped
            .Where(a => a.OperationName == DocProcessingActivities.ProcessStartName)
            .SingleOrDefault(a => a.GetTagItem(DocProcessingActivities.DocumentIdTag) is Guid g && g == docId);
        startActivity.Should().NotBeNull("Ingested transition must emit document.process");
        startActivity!.GetTagItem(DocProcessingActivities.BatchIdTag).Should().Be("batch-42");
    }

    [Fact]
    public async Task ClassificationCompleted_Emits_Success_Outcome_Activity()
    {
        using var capture = new CapturingListener();

        await using var provider = BuildHarnessProvider();
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
            [new IntentResult("change_of_address", [1], 0.9, new Dictionary<string, string?>())],
            DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<ClassificationCompletedEvent>()).Should().BeTrue();

        await harness.Stop();

        var completion = capture.Stopped
            .SingleOrDefault(a =>
                a.OperationName == DocProcessingActivities.ProcessCompletedName &&
                a.GetTagItem(DocProcessingActivities.DocumentIdTag) is Guid g && g == docId);
        completion.Should().NotBeNull("Persisted transition must emit document.process.completed");
        completion!.GetTagItem(DocProcessingActivities.OutcomeTag).Should().Be("success");
        completion.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public async Task DocumentFailed_Emits_Failed_Outcome_Activity_With_Stage_And_Error()
    {
        using var capture = new CapturingListener();

        await using var provider = BuildHarnessProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var docId = Guid.NewGuid();
        var sagaHarness = harness.GetSagaStateMachineHarness<DocumentSaga, DocumentSagaState>();

        await harness.Bus.Publish(new DocumentIngestedEvent(
            docId, "batch-1", "documents/x.tif", "x.tif", DateTimeOffset.UtcNow));
        (await sagaHarness.Consumed.Any<DocumentIngestedEvent>()).Should().BeTrue();

        await harness.Bus.Publish(new DocumentFailedEvent(
            docId, Stage: "rate-limited", Error: "GitHub Models 429", StackTrace: null));
        (await sagaHarness.Consumed.Any<DocumentFailedEvent>()).Should().BeTrue();

        await harness.Stop();

        var completion = capture.Stopped
            .SingleOrDefault(a =>
                a.OperationName == DocProcessingActivities.ProcessCompletedName &&
                a.GetTagItem(DocProcessingActivities.DocumentIdTag) is Guid g && g == docId);
        completion.Should().NotBeNull("DocFailed must emit document.process.completed");
        completion!.GetTagItem(DocProcessingActivities.OutcomeTag).Should().Be("failed");
        completion.GetTagItem(DocProcessingActivities.FailureStageTag).Should().Be("rate-limited");
        completion.GetTagItem(DocProcessingActivities.FailureErrorTag).Should().Be("GitHub Models 429");
        completion.Status.Should().Be(ActivityStatusCode.Error);
    }
}
