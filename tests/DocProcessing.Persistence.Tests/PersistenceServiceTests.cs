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
    public async Task SaveClassificationAsync_Picks_Highest_Confidence_As_TransactionType_And_Merges_Fields()
    {
        Skip.If(_dockerUnavailable, "Docker not available");

        await using var db = await CreateAndMigrateContext();
        var service = new PersistenceService(db, NullLogger<PersistenceService>.Instance);

        var docId = Guid.NewGuid();
        var evt = new ClassificationCompletedEvent(
            docId,
            Intents:
            [
                new IntentResult("change_of_address", [1], 0.65, new Dictionary<string, string?>
                {
                    ["holder_id"] = "111",
                    ["effective_date"] = "2026-06-01",
                }),
                new IntentResult("bereavement", [1], 0.92, new Dictionary<string, string?>
                {
                    ["holder_id"] = "222",   // higher-confidence overrides
                    ["date_of_death"] = "2026-04-20",
                }),
            ],
            CompletedAt: DateTimeOffset.UtcNow);

        await service.SaveClassificationAsync(evt, default);

        var row = await db.ClassificationRecords.SingleAsync(r => r.DocumentId == docId);
        row.TransactionType.Should().Be("bereavement");
        row.Confidence.Should().BeApproximately(0.92, 0.001);

        row.Intents.Should().Contain("change_of_address").And.Contain("bereavement");
        row.ExtractedFields.Should().Contain("\"holder_id\":\"222\"");          // bereavement wins
        row.ExtractedFields.Should().Contain("\"effective_date\":\"2026-06-01\"");
        row.ExtractedFields.Should().Contain("\"date_of_death\":\"2026-04-20\"");
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
            [new IntentResult("deposit_cheque", [1], 0.5, new Dictionary<string, string?>())],
            DateTimeOffset.UtcNow), default);

        // Re-publish (saga retry) with a refined classification.
        await service.SaveClassificationAsync(new ClassificationCompletedEvent(
            docId,
            [new IntentResult("buy_sell_shares", [1], 0.88, new Dictionary<string, string?>
            {
                ["holder_id"] = "333",
            })],
            DateTimeOffset.UtcNow), default);

        var rows = await db.ClassificationRecords.Where(r => r.DocumentId == docId).ToListAsync();
        rows.Should().ContainSingle();
        rows[0].TransactionType.Should().Be("buy_sell_shares");
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
