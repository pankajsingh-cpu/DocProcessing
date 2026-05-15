using Azure.Storage.Blobs;
using DocProcessing.Persistence.Archive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;

namespace DocProcessing.Persistence.Tests;

[Trait("Category", "Integration")]
public sealed class ArchiveServiceTests : IAsyncLifetime
{
    private AzuriteContainer? _azurite;
    private bool _dockerUnavailable;

    public async Task InitializeAsync()
    {
        try
        {
            _azurite = new AzuriteBuilder().Build();
            await _azurite.StartAsync();
        }
        catch (Exception)
        {
            _dockerUnavailable = true;
        }
    }

    public async Task DisposeAsync()
    {
        if (_azurite is not null) await _azurite.DisposeAsync();
    }

    [SkippableFact]
    public async Task ArchiveAsync_Copies_Source_Tif_To_Date_Partitioned_Archive_Path()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        var blobService = new BlobServiceClient(_azurite!.GetConnectionString());
        var container = blobService.GetBlobContainerClient("blobs");
        await container.CreateIfNotExistsAsync();

        var docId = Guid.NewGuid();
        var source = container.GetBlobClient($"documents/{docId}.tif");
        await source.UploadAsync(BinaryData.FromBytes([0xDE, 0xAD, 0xBE, 0xEF]));

        // Pin the clock so the archive path is deterministic.
        var fixedNow = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedNow);

        var service = new ArchiveService(
            blobService,
            Options.Create(new ArchiveOptions { CopyPollInterval = TimeSpan.FromMilliseconds(50) }),
            timeProvider,
            NullLogger<ArchiveService>.Instance);

        await service.ArchiveAsync(docId, default);

        var archive = container.GetBlobClient($"archive/2026/05/{docId}.tif");
        (await archive.ExistsAsync()).Value.Should().BeTrue();

        var sourceBytes = (await source.DownloadContentAsync()).Value.Content.ToArray();
        var archiveBytes = (await archive.DownloadContentAsync()).Value.Content.ToArray();
        archiveBytes.Should().Equal(sourceBytes);
    }

    [SkippableFact]
    public async Task ArchiveAsync_Skips_When_Source_Blob_Does_Not_Exist()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        var blobService = new BlobServiceClient(_azurite!.GetConnectionString());
        var container = blobService.GetBlobContainerClient("blobs");
        await container.CreateIfNotExistsAsync();

        var docId = Guid.NewGuid();
        var service = new ArchiveService(
            blobService,
            Options.Create(new ArchiveOptions()),
            TimeProvider.System,
            NullLogger<ArchiveService>.Instance);

        // Should not throw; saga still considers the document persisted.
        await service.ArchiveAsync(docId, default);

        // Sanity: no archive blob was created.
        var anyArchive = false;
        await foreach (var item in container.GetBlobsAsync(prefix: "archive/"))
        {
            if (item.Name.EndsWith($"{docId}.tif"))
            {
                anyArchive = true;
                break;
            }
        }
        anyArchive.Should().BeFalse();
    }

    [SkippableFact]
    public async Task ArchiveAsync_Is_Idempotent_When_Destination_Already_Exists()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        var blobService = new BlobServiceClient(_azurite!.GetConnectionString());
        var container = blobService.GetBlobContainerClient("blobs");
        await container.CreateIfNotExistsAsync();

        var docId = Guid.NewGuid();
        await container.GetBlobClient($"documents/{docId}.tif")
            .UploadAsync(BinaryData.FromBytes([0x01, 0x02]));

        var fixedNow = new DateTimeOffset(2026, 5, 14, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var service = new ArchiveService(
            blobService,
            Options.Create(new ArchiveOptions { CopyPollInterval = TimeSpan.FromMilliseconds(50) }),
            timeProvider,
            NullLogger<ArchiveService>.Instance);

        await service.ArchiveAsync(docId, default);
        await service.ArchiveAsync(docId, default);   // second call must be a no-op, not a failure

        var archive = container.GetBlobClient($"archive/2026/05/{docId}.tif");
        (await archive.ExistsAsync()).Value.Should().BeTrue();
    }

    // Minimal TimeProvider for tests — System.TimeProvider is abstract, so we
    // subclass and override only what ArchiveService reads (UtcNow + Delay).
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
