using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelf.Core.Data;

namespace Shelf.IntegrationTests;

/// <summary>
/// What the API does to a brand-new database when it starts. Uses a database no other test touches, so nothing
/// else can have migrated it first.
/// </summary>
public class StartupTests
{
    private readonly string _database = $"ShelfStartup_{Guid.NewGuid():N}";

    [Test]
    public async Task Health_is_503_without_the_schema_and_the_API_applies_every_committed_migration_at_startup()
    {
        // 1. Migrations off: the database does not exist, so the SQL Server health check fails.
        using (var cold = new ShelfApiFactory(_database, migrate: false))
        {
            using var client = cold.CreateClient();
            var health = await client.GetAsync("/health");
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

            using var scope = cold.Services.CreateScope();
            Assert.That(await scope.ServiceProvider.GetRequiredService<ShelfDbContext>().Database.CanConnectAsync(), Is.False,
                "the database must not exist before the migrating start");
        }

        // 2. Migrations on (the default): DatabaseMigrator runs before the first request.
        using var factory = new ShelfApiFactory(_database);
        using var api = factory.CreateClient();
        using var db = factory.Services.CreateScope();
        var context = db.ServiceProvider.GetRequiredService<ShelfDbContext>();

        var committed = context.Database.GetMigrations().ToList();
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(committed, Has.Some.EndsWith("_InitialCreate").And.Some.EndsWith("_TransactionalOutbox"));
            Assert.That(applied, Is.EqualTo(committed));
            Assert.That(await context.Database.GetPendingMigrationsAsync(), Is.Empty);
            Assert.That(await context.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync(), Is.Zero, "outbox table exists");
            Assert.That((await api.GetAsync("/health")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }
}
