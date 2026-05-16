using DocProcessing.Contracts.Events;
using DocProcessing.Ingest.Service;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Service.Tests;

public class BatchArrivedConsumerTests
{
    private sealed class FakeUploader : IBlobUploader
    {
        public List<(string Path, byte[] Bytes)> Uploads { get; } = new();
        public Task UploadAsync(string blobPath, byte[] content, CancellationToken ct)
        {
            Uploads.Add((blobPath, content));
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(".tif")]
    [InlineData(".tiff")]
    [InlineData(".pdf")]
    public async Task Publishes_Single_DocumentIngestedEvent_Preserving_Extension(string ext)
    {
        var uploader = new FakeUploader();
        var opts = Options.Create(new IngestOptions { ContainerName = "blobs", DocumentPrefix = "documents" });

        await using var provider = new ServiceCollection()
            .AddSingleton<IBlobUploader>(uploader)
            .AddSingleton(opts)
            .AddMassTransitTestHarness(x => x.AddConsumer<BatchArrivedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var tempPath = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(tempPath, [0x01, 0x02, 0x03, 0x04, 0x05]);

        try
        {
            await harness.Bus.Publish(new BatchArrivedEvent(
                BatchId: Path.GetFileName(tempPath),
                SourcePath: tempPath,
                ArrivedAt: DateTimeOffset.UtcNow));

            var consumerHarness = harness.GetConsumerHarness<BatchArrivedConsumer>();
            (await consumerHarness.Consumed.Any<BatchArrivedEvent>()).Should().BeTrue();

            var published = await harness.Published.SelectAsync<DocumentIngestedEvent>().ToListAsync();
            published.Should().ContainSingle();

            var msg = published[0].Context.Message;
            msg.BatchId.Should().Be(Path.GetFileName(tempPath));
            msg.SourceFileName.Should().Be(Path.GetFileName(tempPath));
            msg.BlobPath.Should().StartWith("documents/").And.EndWith(ext);
            msg.DocumentId.Should().NotBe(Guid.Empty);

            uploader.Uploads.Should().ContainSingle();
            uploader.Uploads[0].Path.Should().Be(msg.BlobPath);
            uploader.Uploads[0].Bytes.Should().Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
        }
        finally
        {
            File.Delete(tempPath);
            await harness.Stop();
        }
    }

    [Fact]
    public async Task Ignores_Unsupported_Extension_Without_Publishing()
    {
        var uploader = new FakeUploader();
        var opts = Options.Create(new IngestOptions
        {
            ContainerName = "blobs",
            DocumentPrefix = "documents",
            // .txt is not in the supported list — should be dropped silently.
            SupportedExtensions = [".tif", ".tiff", ".pdf"],
        });

        await using var provider = new ServiceCollection()
            .AddSingleton<IBlobUploader>(uploader)
            .AddSingleton(opts)
            .AddMassTransitTestHarness(x => x.AddConsumer<BatchArrivedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var tempPath = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempPath, "noise");

        try
        {
            await harness.Bus.Publish(new BatchArrivedEvent(
                BatchId: Path.GetFileName(tempPath),
                SourcePath: tempPath,
                ArrivedAt: DateTimeOffset.UtcNow));

            var consumerHarness = harness.GetConsumerHarness<BatchArrivedConsumer>();
            (await consumerHarness.Consumed.Any<BatchArrivedEvent>()).Should().BeTrue();

            (await harness.Published.Any<DocumentIngestedEvent>()).Should().BeFalse();
            uploader.Uploads.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempPath);
            await harness.Stop();
        }
    }
}
