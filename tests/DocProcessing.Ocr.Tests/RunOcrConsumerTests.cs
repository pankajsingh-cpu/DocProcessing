using System.Text.Json;
using Azure.Storage.Blobs;
using DocProcessing.Contracts.Commands;
using DocProcessing.Contracts.Events;
using DocProcessing.Ocr;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;

namespace DocProcessing.Ocr.Tests;

[Trait("Category", "Integration")]
public class RunOcrConsumerTests : IAsyncLifetime
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

    private sealed class FailingEngine : IOcrEngine
    {
        public Task<OcrResult> AnalyseAsync(Guid documentId, byte[] sourceBytes, CancellationToken ct)
            => throw new InvalidOperationException("simulated OCR failure");
    }

    [SkippableFact]
    public async Task Happy_Path_Uploads_Ocr_Json_And_Publishes_OcrCompletedEvent()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        var blobService = new BlobServiceClient(_azurite!.GetConnectionString());
        var container = blobService.GetBlobContainerClient("blobs");
        await container.CreateIfNotExistsAsync();

        // Seed the source blob the consumer will download.
        var docId = Guid.NewGuid();
        var sourcePath = $"documents/{docId}.tif";
        await container.GetBlobClient(sourcePath).UploadAsync(BinaryData.FromBytes([0xDE, 0xAD, 0xBE, 0xEF]));

        // Fixture dir with a 2-page response.
        var fixtureDir = Path.Combine(Path.GetTempPath(), "dp-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDir);
        await File.WriteAllTextAsync(
            Path.Combine(fixtureDir, $"{docId}.json"),
            """{"modelId":"prebuilt-layout","pages":[{"pageNumber":1,"content":"a"},{"pageNumber":2,"content":"b"}]}""");

        try
        {
            await using var provider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(blobService)
                .AddSingleton(Options.Create(new OcrOptions
                {
                    Mode = "stub",
                    ContainerName = "blobs",
                    OcrPrefix = "ocr",
                    FixturePath = fixtureDir,
                }))
                .AddSingleton<IOcrEngine, StubOcrEngine>()
                .AddMassTransitTestHarness(x => x.AddConsumer<RunOcrConsumer>())
                .BuildServiceProvider(true);

            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            await harness.Bus.Publish(new RunOcrCommand(docId, sourcePath));

            var consumerHarness = harness.GetConsumerHarness<RunOcrConsumer>();
            (await consumerHarness.Consumed.Any<RunOcrCommand>()).Should().BeTrue();

            var ocrBlob = container.GetBlobClient($"ocr/{docId}.json");
            (await ocrBlob.ExistsAsync()).Value.Should().BeTrue("OCR JSON should land in blob storage");

            var published = await harness.Published
                .SelectAsync<OcrCompletedEvent>()
                .ToListAsync();
            published.Should().ContainSingle()
                .Which.Context.Message.PageCount.Should().Be(2);

            await harness.Stop();
        }
        finally
        {
            try { Directory.Delete(fixtureDir, recursive: true); } catch { }
        }
    }

    [SkippableFact]
    public async Task Engine_Failure_Publishes_DocumentFailedEvent_With_Stage_Ocr()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        var blobService = new BlobServiceClient(_azurite!.GetConnectionString());
        var container = blobService.GetBlobContainerClient("blobs");
        await container.CreateIfNotExistsAsync();

        var docId = Guid.NewGuid();
        var sourcePath = $"documents/{docId}.tif";
        await container.GetBlobClient(sourcePath).UploadAsync(BinaryData.FromBytes([0x00]));

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton(blobService)
            .AddSingleton(Options.Create(new OcrOptions { ContainerName = "blobs", OcrPrefix = "ocr" }))
            .AddSingleton<IOcrEngine, FailingEngine>()
            .AddMassTransitTestHarness(x => x.AddConsumer<RunOcrConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new RunOcrCommand(docId, sourcePath));

        var consumerHarness = harness.GetConsumerHarness<RunOcrConsumer>();
        (await consumerHarness.Consumed.Any<RunOcrCommand>()).Should().BeTrue();

        var failed = await harness.Published
            .SelectAsync<DocumentFailedEvent>(c => c.Context.Message.DocumentId == docId)
            .ToListAsync();
        failed.Should().ContainSingle()
            .Which.Context.Message.Stage.Should().Be("ocr");

        // No OcrCompletedEvent should have been published.
        (await harness.Published.Any<OcrCompletedEvent>(c => c.Context.Message.DocumentId == docId))
            .Should().BeFalse();

        await harness.Stop();
    }
}
