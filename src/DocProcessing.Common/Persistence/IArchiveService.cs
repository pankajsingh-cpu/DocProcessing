namespace DocProcessing.Common.Persistence;

// Implemented locally by DocProcessing.Persistence.Archive (see Prompt 10).
// Copies documents/{id}.tif to archive/{yyyy}/{MM}/{id}.tif.
public interface IArchiveService
{
    Task ArchiveAsync(Guid documentId, CancellationToken ct);
}
