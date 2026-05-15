using MassTransit;

namespace DocProcessing.Orchestration;

public class DocumentSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public int Version { get; set; }
    public string CurrentState { get; set; } = default!;

    public string? BatchId { get; set; }
    public string? SourceBlobPath { get; set; }
    public string? OcrBlobPath { get; set; }

    public DateTime? OcrTimeout { get; set; }
    public DateTime? ClassifyTimeout { get; set; }

    public DateTimeOffset? IngestedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureStage { get; set; }
    public string? FailureError { get; set; }
}
