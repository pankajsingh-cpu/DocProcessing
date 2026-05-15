using MassTransit;

namespace DocProcessing.Common.Observability;

public static class MassTransitObservabilityExtensions
{
    // Drop into any `UsingRabbitMq` / `UsingAzureServiceBus` lambda right before
    // ConfigureEndpoints so every consumer gets the DocumentId log scope.
    public static void UseDocProcDocumentIdLogScope(
        this IConsumePipeConfigurator cfg,
        IRegistrationContext context)
    {
        cfg.UseConsumeFilter(typeof(DocumentIdLogScopeFilter<>), context);
    }
}
