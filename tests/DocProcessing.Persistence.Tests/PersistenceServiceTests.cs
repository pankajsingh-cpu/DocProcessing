using System.Text.Json;
using DocProcessing.Contracts.Events;
using DocProcessing.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;

namespace DocProcessing.Persistence.Tests;

[Trait("Category", "Integration")]
public sealed class PersistenceServiceTests : IAsyncLifetime
{
    private MsSqlContainer? _sql;
    private bool _dockerUnavailable;

    public async Task InitializeAsync()
    {
        try
        {
            _sql = new MsSqlBuilder().Build();
            await _sql.StartAsync();
        }
        catch (Exception)
        {
            _dockerUnavailable = true;
        }
    }

    public async Task DisposeAsync()
    {
        if (_sql is not null) await _sql.DisposeAsync();
    }

    [SkippableFact]
    public async Task SaveClassificationAsync_Picks_Highest_Confidence_And_Stores_Top_Payload()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        await using var db = await CreateAndMigrateContext();
        var service = new PersistenceService(db, NullLogger<PersistenceService>.Instance);

        var dripPayload = JsonDocument.Parse("""
            {"transaction_type":"drip_ocp","holder":{"holder_id":"111"},"extracted":{"amount_number":"150.00"}}
            """).RootElement;
        var sellPayload = JsonDocument.Parse("""
            {"transaction_type":"sell_stock","sale":[{"holder_name":"M. O'Reilly","account_number":"9988"}]}
            """).RootElement;

        var docId = Guid.NewGuid();
        var evt = new ClassificationCompletedEvent(
            docId,
            Intents:
            [
                new IntentResult("drip_ocp",   0.65, dripPayload),
                new IntentResult("sell_stock", 0.92, sellPayload),   // higher confidence wins
            ],
            CompletedAt: DateTimeOffset.UtcNow);

        await service.SaveClassificationAsync(evt, default);

        var row = await db.ClassificationRecords.SingleAsync(r => r.DocumentId == docId);
        row.TransactionType.Should().Be("sell_stock");
        row.Confidence.Should().BeApproximately(0.92, 0.001);

        // Intents column carries the full list including both candidates.
        row.Intents.Should().Contain("drip_ocp").And.Contain("sell_stock");

        // ExtractedFields column holds the top intent's payload verbatim
        // (now the typed sell_stock shape, not a merged flat dict).
        row.ExtractedFields.Should().Contain("\"transaction_type\":\"sell_stock\"");
        row.ExtractedFields.Should().Contain("\"holder_name\":\"M. O'Reilly\"");
        row.ExtractedFields.Should().NotContain("drip_ocp");
    }

    [SkippableFact]
    public async Task SaveClassificationAsync_Is_Idempotent_On_Repeat()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        await using var db = await CreateAndMigrateContext();
        var service = new PersistenceService(db, NullLogger<PersistenceService>.Instance);

        var docId = Guid.NewGuid();

        await service.SaveClassificationAsync(new ClassificationCompletedEvent(
            docId,
            [new IntentResult("drip_ocp", 0.5,
                JsonDocument.Parse("""{"transaction_type":"drip_ocp"}""").RootElement)],
            DateTimeOffset.UtcNow), default);

        // Re-publish (saga retry) with a refined classification.
        await service.SaveClassificationAsync(new ClassificationCompletedEvent(
            docId,
            [new IntentResult("sell_stock", 0.88,
                JsonDocument.Parse("""{"transaction_type":"sell_stock","sale":[{"account_number":"333"}]}""").RootElement)],
            DateTimeOffset.UtcNow), default);

        var rows = await db.ClassificationRecords.Where(r => r.DocumentId == docId).ToListAsync();
        rows.Should().ContainSingle();
        rows[0].TransactionType.Should().Be("sell_stock");
        rows[0].ExtractedFields.Should().Contain("\"account_number\":\"333\"");
    }

    private async Task<PersistenceDbContext> CreateAndMigrateContext()
    {
        var options = new DbContextOptionsBuilder<PersistenceDbContext>()
            .UseSqlServer(
                _sql!.GetConnectionString(),
                sql => sql.MigrationsHistoryTable(PersistenceDbContext.MigrationsHistoryTable))
            .Options;

        var db = new PersistenceDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }
}
