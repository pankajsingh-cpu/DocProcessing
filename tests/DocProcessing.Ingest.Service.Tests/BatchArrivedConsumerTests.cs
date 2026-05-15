using DocProcessing.Contracts.Events;
using DocProcessing.Ingest.Service;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Service.Tests;

public class BatchArrivedConsumerTests
{
    private sealed class FakeSplitter(int pageCount) : ITifSplitter
    {
        public List<int> ObservedReads { get; } = new();

        public async IAsyncEnumerable<DocumentPage> SplitAsync(
            Stream source,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            ObservedReads.Add((int)source.Length);
            for (var i = 1; i <= pageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return new DocumentPage(i, new byte[] { (byte)i });
                await Task.Yield();
            }
        }
    }

    private sealed class FakeUploader : IBlobUploader
    {
        public List<(string Path, byte[] Bytes)> Uploads { get; } = new();
        public Task UploadAsync(string blobPath, byte[] content, CancellationToken ct)
        {
            Uploads.Add((blobPath, content));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Publishes_One_DocumentIngestedEvent_Per_Page_With_Correct_Shape()
    {
        const int pages = 3;

        var splitter = new FakeSplitter(pages);
        var uploader = new FakeUploader();
        var opts = Options.Create(new IngestOptions { ContainerName = "blobs", DocumentPrefix = "documents" });

        await using var provider = new ServiceCollection()
            .AddSingleton<ITifSplitter>(splitter)
            .AddSingleton<IBlobUploader>(uploader)
            .AddSingleton(opts)
            .AddMassTransitTestHarness(x => x.AddConsumer<BatchArrivedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Create a small temp TIF — the consumer just opens it; the FakeSplitter
        // doesn't actually decode it.
        var tempPath = Path.Combine(Path.GetTempPath(), $"batch-{Guid.NewGuid():N}.tif");
        await File.WriteAllBytesAsync(tempPath, [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00]);

        try
        {
            await harness.Bus.Publish(new BatchArrivedEvent(
                BatchId: "batch-1",
                SourcePath: tempPath,
                ArrivedAt: DateTimeOffset.UtcNow));

            var consumerHarness = harness.GetConsumerHarness<BatchArrivedConsumer>();
            (await consumerHarness.Consumed.Any<BatchArrivedEvent>()).Should().BeTrue();

            // 3 DocumentIngestedEvents published
            var published = await harness.Published
                .SelectAsync<DocumentIngestedEvent>()
                .ToListAsync();
            published.Should().HaveCount(pages);

            // Each event's BlobPath matches what was uploaded
            var uploadedPaths = uploader.Uploads.Select(u => u.Path).ToArray();
            var eventPaths = published.Select(p => p.Context.Message.BlobPath).ToArray();
            eventPaths.Should().BeEquivalentTo(uploadedPaths);

            // Shape checks
            foreach (var msg in published.Select(p => p.Context.Message))
            {
                msg.BatchId.Should().Be("batch-1");
                msg.BlobPath.Should().StartWith("documents/").And.EndWith(".tif");
                msg.SourceFileName.Should().Be(Path.GetFileName(tempPath));
                msg.DocumentId.Should().NotBe(Guid.Empty);
                msg.IngestedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            }

            uploader.Uploads.Should().HaveCount(pages);
        }
        finally
        {
            File.Delete(tempPath);
            await harness.Stop();
        }
    }
}
