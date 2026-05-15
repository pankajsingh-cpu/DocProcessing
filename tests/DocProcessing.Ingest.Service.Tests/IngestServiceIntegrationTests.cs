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

    [SkippableFact]
    public async Task End_To_End_BatchArrived_Produces_Blobs_And_Events()
    {
        Skip.If(_dockerUnavailable, "Docker not available locally; skipping Testcontainers integration test.");

        // Arrange: real Azurite + real splitter + real uploader, fake bus via test harness.
        var connectionString = _azurite!.GetConnectionString();
        var blobService = new BlobServiceClient(connectionString);

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(blobService)
            .AddSingleton<ITifSplitter, TifSplitter>()
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

        // Write a 2-page TIF to disk that the consumer will open by path.
        var tempPath = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}.tif");
        await File.WriteAllBytesAsync(tempPath, TifFixtures.CreateMultiPageTif(2));

        try
        {
            await harness.Bus.Publish(new BatchArrivedEvent(
                BatchId: Path.GetFileName(tempPath),
                SourcePath: tempPath,
                ArrivedAt: DateTimeOffset.UtcNow));

            var consumerHarness = harness.GetConsumerHarness<BatchArrivedConsumer>();
            (await consumerHarness.Consumed.Any<BatchArrivedEvent>()).Should().BeTrue();

            var ingested = await harness.Published
                .SelectAsync<DocumentIngestedEvent>()
                .ToListAsync();
            ingested.Should().HaveCount(2);

            // Each blob path returned in the event should exist in Azurite.
            var container = blobService.GetBlobContainerClient("blobs");
            foreach (var msg in ingested.Select(p => p.Context.Message))
            {
                var blob = container.GetBlobClient(msg.BlobPath);
                (await blob.ExistsAsync()).Value.Should().BeTrue($"blob {msg.BlobPath} should exist after upload");
            }
        }
        finally
        {
            File.Delete(tempPath);
            await harness.Stop();
        }
    }
}
