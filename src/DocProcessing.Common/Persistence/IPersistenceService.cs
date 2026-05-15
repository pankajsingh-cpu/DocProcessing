using DocProcessing.Contracts.Events;

namespace DocProcessing.Common.Persistence;

// Implemented locally by DocProcessing.Persistence.Sql (see Prompt 10).
// In cloud, this becomes a published PersistDocumentCommand consumed by a dedicated pod.
public interface IPersistenceService
{
    Task SaveClassificationAsync(ClassificationCompletedEvent evt, CancellationToken ct);
}
