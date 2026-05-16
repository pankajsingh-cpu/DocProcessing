using DocProcessing.Common.Persistence;
using DocProcessing.Contracts.Commands;
using DocProcessing.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocProcessing.Orchestration;

public class DocumentSaga : MassTransitStateMachine<DocumentSagaState>
{
    public State AwaitingOcr { get; } = null!;
    public State AwaitingClassification { get; } = null!;
    public State Persisted { get; } = null!;
    public State Failed { get; } = null!;

    public Event<DocumentIngestedEvent> Ingested { get; } = null!;
    public Event<OcrCompletedEvent> OcrDone { get; } = null!;
    public Event<ClassificationCompletedEvent> ClassifyDone { get; } = null!;
    public Event<DocumentFailedEvent> DocFailed { get; } = null!;

    public DocumentSaga()
    {
        InstanceState(x => x.CurrentState);

        Event(() => Ingested, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => OcrDone, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => ClassifyDone, x => x.CorrelateById(c => c.Message.DocumentId));
        Event(() => DocFailed, x => x.CorrelateById(c => c.Message.DocumentId));

        Initially(
            When(Ingested)
                .Then(ctx =>
                {
                    ctx.Saga.BatchId = ctx.Message.BatchId;
                    ctx.Saga.SourceBlobPath = ctx.Message.BlobPath;
                    ctx.Saga.IngestedAt = ctx.Message.IngestedAt;
                    ctx.Saga.OcrTimeout = DateTime.UtcNow.AddMinutes(2);

                    using var _ = DocProcessingActivities.StartProcessing(
                        ctx.Message.DocumentId, ctx.Message.BatchId, ctx.Message.BlobPath);
                })
                .Send(new Uri("queue:ocr"),
                      ctx => new RunOcrCommand(ctx.Message.DocumentId, ctx.Message.BlobPath))
                .TransitionTo(AwaitingOcr));

        During(AwaitingOcr,
            When(OcrDone)
                .Then(ctx =>
                {
                    ctx.Saga.OcrBlobPath = ctx.Message.OcrResultBlobPath;
                    ctx.Saga.ClassifyTimeout = DateTime.UtcNow.AddMinutes(1);
                })
                .Send(new Uri("queue:classify"),
                      ctx => new ClassifyCommand(ctx.Message.DocumentId, ctx.Message.OcrResultBlobPath))
                .TransitionTo(AwaitingClassification));

        During(AwaitingClassification,
            When(ClassifyDone)
                .ThenAsync(PersistAndArchive)
                .TransitionTo(Persisted)
                .Finalize());

        // Any state: explicit failure -> route to DLQ + Failed
        DuringAny(
            When(DocFailed)
                .Then(ctx =>
                {
                    ctx.Saga.FailureStage = ctx.Message.Stage;
                    ctx.Saga.FailureError = ctx.Message.Error;

                    using var _ = DocProcessingActivities.MarkFailed(
                        ctx.Message.DocumentId, ctx.Message.Stage, ctx.Message.Error);
                })
                .Send(new Uri("queue:dlq"),
                      ctx => new DocumentFailedEvent(
                          ctx.Message.DocumentId,
                          ctx.Message.Stage,
                          ctx.Message.Error,
                          ctx.Message.StackTrace))
                .TransitionTo(Failed));

        SetCompletedWhenFinalized();
    }

    private static async Task PersistAndArchive(BehaviorContext<DocumentSagaState, ClassificationCompletedEvent> ctx)
    {
        var sp = ctx.GetPayload<IServiceProvider>();
        var logger = sp.GetRequiredService<ILogger<DocumentSaga>>();
        var persistence = sp.GetRequiredService<IPersistenceService>();
        var archive = sp.GetRequiredService<IArchiveService>();

        await persistence.SaveClassificationAsync(ctx.Message, ctx.CancellationToken);

        // SourceBlobPath was stashed on Ingested. It already carries the right
        // extension (.tif/.tiff/.pdf) — archive uses it to derive the destination.
        var sourceBlobPath = ctx.Saga.SourceBlobPath
            ?? throw new InvalidOperationException(
                $"Saga {ctx.Saga.CorrelationId} has no SourceBlobPath; ingest never recorded one.");
        await archive.ArchiveAsync(ctx.Message.DocumentId, sourceBlobPath, ctx.CancellationToken);

        ctx.Saga.CompletedAt = ctx.Message.CompletedAt;

        using var _ = DocProcessingActivities.MarkSucceeded(ctx.Message.DocumentId);
        logger.LogInformation("Persisted document {DocumentId}", ctx.Message.DocumentId);
    }
}
