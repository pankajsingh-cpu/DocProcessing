namespace DocProcessing.Common.Persistence;

// Implemented locally by DocProcessing.Persistence.Archive (see Prompt 10).
// Copies the source blob to archive/{yyyy}/{MM}/{id}{ext}, preserving the
// original extension (.tif / .tiff / .pdf — anything the ingest accepts).
public interface IArchiveService
{
    Task ArchiveAsync(Guid documentId, string sourceBlobPath, CancellationToken ct);
}
