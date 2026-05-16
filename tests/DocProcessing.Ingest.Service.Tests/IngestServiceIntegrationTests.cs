using Azure.Storage.Blobs;
using DocProcessing.Contracts.Events;
using DocProcessing.Ingest.Service;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;

namespace DocProcessing.Ingest.Service.Tests;

[Trait("Category", "Integration")]
public class IngestServiceIntegrationTests : IAsyncLifetime
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

    [SkippableTheory]
    [InlineData(".tif")]
    [InlineData(".pdf")]
    public async Task End_To_End_BatchArrived_Uploads_Blob_And_Publishes_Event(string ext)
    {
        Skip.If(_dockerUnavailable, "Docker not available locally; skipping Testcontainers integration test.");

        var connectionString = _azurite!.GetConnectionString();
        var blobService = new BlobServiceClient(connectionString);

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(blobService)
            .AddSingleton<IBlobUploader, BlobUploader>()
            .AddSingleton(Options.Create(new IngestOptions
            {
                ContainerName = "blobs",
                DocumentPrefix = "documents",
            }))
            .AddMassTransitTestHarness(x => x.AddConsumer<BatchArrivedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Opaque bytes — ingest no longer parses files, only uploads them.
        // Real PDFs/TIFs flow through the same code path.
        var tempPath = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(tempPath, [0xDE, 0xAD, 0xBE, 0xEF]);

        try
        {
            await harness.Bus.Publish(new BatchArrivedEvent(
                BatchId: Path.GetFileName(tempPath),
                SourcePath: tempPath,
                ArrivedAt: DateTimeOffset.UtcNow));

            var consumerHarness = harness.GetConsumerHarness<BatchArrivedConsumer>();
            (await consumerHarness.Consumed.Any<BatchArrivedEvent>()).Should().BeTrue();

            var ingested = await harness.Published.SelectAsync<DocumentIngestedEvent>().ToListAsync();
            ingested.Should().ContainSingle();

            var msg = ingested[0].Context.Message;
            msg.BlobPath.Should().EndWith(ext);

            var container = blobService.GetBlobContainerClient("blobs");
            var blob = container.GetBlobClient(msg.BlobPath);
            (await blob.ExistsAsync()).Value.Should().BeTrue($"blob {msg.BlobPath} should exist after upload");

            var content = (await blob.DownloadContentAsync()).Value.Content.ToArray();
            content.Should().Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        }
        finally
        {
            File.Delete(tempPath);
            await harness.Stop();
        }
    }
}
