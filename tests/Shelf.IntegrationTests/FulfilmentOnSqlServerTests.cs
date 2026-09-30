using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shelf.Core.Data;
using Shelf.Fulfilment;

namespace Shelf.IntegrationTests;

/// <summary>
/// The worker's idempotency on real SQL Server, where the ProcessedMessages primary key is enforced.
/// The schema comes from the same committed migrations the API applies.
/// </summary>
public class FulfilmentOnSqlServerTests
{
    private DbContextOptions<ShelfDbContext> _options = null!;

    [OneTimeSetUp]
    public async Task Migrate()
    {
        _options = new DbContextOptionsBuilder<ShelfDbContext>()
            .UseSqlServer(SqlServerFixture.ConnectionString)
            .Options;
        await using var db = new ShelfDbContext(_options);
        await db.Database.MigrateAsync();
    }

    private async Task<Guid> SeedPlacedOrderAsync()
    {
        var id = Guid.NewGuid();
        await using var db = new ShelfDbContext(_options);
        db.Orders.Add(new Order { Id = id, Email = "reader@example.com", Status = OrderStatus.Placed, Total = 7.99m, PlacedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private static OrderFulfilment Handler(ShelfDbContext db, int simulatedWorkMs) =>
        new(db, TimeProvider.System, Options.Create(new FulfilmentOptions { SimulatedWorkMs = simulatedWorkMs }), NullLogger<OrderFulfilment>.Instance);

    [Test]
    public async Task Two_copies_of_the_same_message_racing_fulfil_the_order_exactly_once()
    {
        var orderId = await SeedPlacedOrderAsync();

        // Both copies pass the "already processed?" check before either saves (the simulated work holds them
        // there), so the only thing stopping a double fulfilment is the primary key on ProcessedMessages.
        await using var db1 = new ShelfDbContext(_options);
        await using var db2 = new ShelfDbContext(_options);
        var results = await Task.WhenAll(
            Handler(db1, simulatedWorkMs: 500).HandleAsync(orderId),
            Handler(db2, simulatedWorkMs: 500).HandleAsync(orderId));

        await using var check = new ShelfDbContext(_options);
        Assert.Multiple(async () =>
        {
            Assert.That(results, Is.EquivalentTo(new[] { FulfilmentResult.Fulfilled, FulfilmentResult.AlreadyProcessed }));
            Assert.That(await check.ProcessedMessages.CountAsync(p => p.OrderId == orderId), Is.EqualTo(1));
            Assert.That((await check.Orders.SingleAsync(o => o.Id == orderId)).Status, Is.EqualTo(OrderStatus.Fulfilled));
        });
    }

    [Test]
    public async Task Sequential_redelivery_is_a_no_op()
    {
        var orderId = await SeedPlacedOrderAsync();

        FulfilmentResult first, second;
        await using (var db = new ShelfDbContext(_options))
            first = await Handler(db, 0).HandleAsync(orderId);
        DateTime? fulfilledAt;
        await using (var db = new ShelfDbContext(_options))
            fulfilledAt = (await db.Orders.SingleAsync(o => o.Id == orderId)).FulfilledAtUtc;
        await using (var db = new ShelfDbContext(_options))
            second = await Handler(db, 0).HandleAsync(orderId);

        await using var check = new ShelfDbContext(_options);
        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.EqualTo(FulfilmentResult.Fulfilled));
            Assert.That(second, Is.EqualTo(FulfilmentResult.AlreadyProcessed));
            Assert.That(await check.ProcessedMessages.CountAsync(p => p.OrderId == orderId), Is.EqualTo(1));
            Assert.That((await check.Orders.SingleAsync(o => o.Id == orderId)).FulfilledAtUtc, Is.EqualTo(fulfilledAt));
        });
    }
}
