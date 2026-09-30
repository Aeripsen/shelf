using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    private sealed class FixedTime(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    /// <summary>
    /// The handler reads the clock after its "already processed?" check and just before SaveChanges. Making every
    /// participant wait at a barrier there guarantees both copies passed the check before either one saves.
    /// </summary>
    private sealed class BarrierTime(DateTime utc, Barrier barrier) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("the other handler never reached SaveChanges");
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }
    }

    /// <summary>Keeps every log message so a test can tell which code path ran.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
                Messages.Add(formatter(state, exception));
        }
    }

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

    private static OrderFulfilment Handler(ShelfDbContext db, int simulatedWorkMs, TimeProvider? time = null, ILogger<OrderFulfilment>? log = null) =>
        new(db, time ?? TimeProvider.System, Options.Create(new FulfilmentOptions { SimulatedWorkMs = simulatedWorkMs }), log ?? NullLogger<OrderFulfilment>.Instance);

    [Test]
    public async Task Two_copies_racing_fulfil_the_order_once_and_the_loser_rolls_back()
    {
        var orderId = await SeedPlacedOrderAsync();
        var timeA = new DateTime(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);
        var timeB = timeA.AddMinutes(7);
        var log = new ListLogger<OrderFulfilment>();

        // Both copies read "not processed yet", then meet at the barrier, then both call SaveChanges.
        // Only the primary key on ProcessedMessages stops a double fulfilment.
        using var barrier = new Barrier(2);
        await using var db1 = new ShelfDbContext(_options);
        await using var db2 = new ShelfDbContext(_options);
        var results = await Task.WhenAll(
            Task.Run(() => Handler(db1, 0, new BarrierTime(timeA, barrier), log).HandleAsync(orderId)),
            Task.Run(() => Handler(db2, 0, new BarrierTime(timeB, barrier), log).HandleAsync(orderId)));

        await using var check = new ShelfDbContext(_options);
        var order = await check.Orders.SingleAsync(o => o.Id == orderId);
        var processed = await check.ProcessedMessages.SingleAsync(p => p.OrderId == orderId);
        var winnerTime = results[0] == FulfilmentResult.Fulfilled ? timeA : timeB;

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.EquivalentTo(new[] { FulfilmentResult.Fulfilled, FulfilmentResult.AlreadyProcessed }));
            Assert.That(log.Messages.Count(m => m.Contains("duplicate delivery lost the race")), Is.EqualTo(1),
                "the loser must have reached SaveChanges and been stopped by the primary key, not by the fast-path check");
            Assert.That(order.Status, Is.EqualTo(OrderStatus.Fulfilled));
            Assert.That(order.FulfilledAtUtc, Is.EqualTo(winnerTime), "the loser's update to the order must roll back with its insert");
            Assert.That(processed.ProcessedAtUtc, Is.EqualTo(winnerTime));
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
