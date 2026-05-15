using DocProcessing.Contracts.Commands;
using DocProcessing.Contracts.Events;
using MassTransit;
using SerilogLogContext = Serilog.Context.LogContext;

namespace DocProcessing.Common.Observability;

// Pushes DocumentId into Serilog's LogContext for the duration of a consume,
// so every log entry emitted while a message is being handled (including
// nested calls inside the saga or downstream services) carries the
// {DocumentId} property automatically.
//
// Registered globally via cfg.UseConsumeFilter(typeof(DocumentIdLogScopeFilter<>), ctx)
// so it applies to every consumer + saga without per-message wiring.
public sealed class DocumentIdLogScopeFilter<TMessage> : IFilter<ConsumeContext<TMessage>>
    where TMessage : class
{
    public const string PropertyName = "DocumentId";

    public async Task Send(ConsumeContext<TMessage> context, IPipe<ConsumeContext<TMessage>> next)
    {
        var documentId = TryExtractDocumentId(context.Message);
        if (documentId is null)
        {
            await next.Send(context);
            return;
        }

        using (SerilogLogContext.PushProperty(PropertyName, documentId.Value))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) =>
        context.CreateFilterScope("documentIdLogScope");

    // Pattern match against every contract that exposes DocumentId. Add new
    // message types here as the pipeline grows — anything not listed simply
    // bypasses the filter.
    private static Guid? TryExtractDocumentId(TMessage message) => message switch
    {
        DocumentIngestedEvent e => e.DocumentId,
        OcrCompletedEvent e => e.DocumentId,
        ClassificationCompletedEvent e => e.DocumentId,
        DocumentFailedEvent e => e.DocumentId,
        RunOcrCommand c => c.DocumentId,
        ClassifyCommand c => c.DocumentId,
        _ => null,
    };
}
