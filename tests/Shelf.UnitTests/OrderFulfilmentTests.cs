using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shelf.Core.Data;
using Shelf.Fulfilment;

namespace Shelf.UnitTests;

/// <summary>
/// Idempotency of the fulfilment handler on EF Core's in-memory provider. The in-memory provider does not enforce
/// keys, so the race between two copies (the duplicate-key path) is tested on real SQL Server in the integration tests.
/// </summary>
public class OrderFulfilmentTests
{
    private DbContextOptions<ShelfDbContext> _options = null!;
    private FakeTime _time = null!;

    [SetUp]
    public void SetUp()
    {
        _options = new DbContextOptionsBuilder<ShelfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _time = new FakeTime(new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero));
    }

    private OrderFulfilment NewHandler(ShelfDbContext db) =>
        new(db, _time, Options.Create(new FulfilmentOptions { SimulatedWorkMs = 0 }), NullLogger<OrderFulfilment>.Instance);

    private async Task<Guid> SeedPlacedOrderAsync()
    {
        var id = Guid.NewGuid();
        await using var db = new ShelfDbContext(_options);
        db.Orders.Add(new Order
        {
            Id = id,
            Email = "reader@example.com",
            Status = OrderStatus.Placed,
            Total = 11.50m,
            PlacedAtUtc = _time.Now.UtcDateTime,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Test]
    public async Task First_delivery_fulfils_the_order_and_records_the_message()
    {
        var orderId = await SeedPlacedOrderAsync();
        _time.Now = _time.Now.AddMinutes(1);

        FulfilmentResult result;
        await using (var db = new ShelfDbContext(_options))
            result = await NewHandler(db).HandleAsync(orderId);

        await using var check = new ShelfDbContext(_options);
        var order = await check.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Multiple(async () =>
        {
            Assert.That(result, Is.EqualTo(FulfilmentResult.Fulfilled));
            Assert.That(order.Status, Is.EqualTo(OrderStatus.Fulfilled));
            Assert.That(order.FulfilledAtUtc, Is.EqualTo(_time.Now.UtcDateTime));
            Assert.That(await check.ProcessedMessages.CountAsync(p => p.OrderId == orderId), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Redelivered_message_is_a_no_op()
    {
        var orderId = await SeedPlacedOrderAsync();
        var firstTime = _time.Now.AddMinutes(1);
        _time.Now = firstTime;

        await using (var db = new ShelfDbContext(_options))
            await NewHandler(db).HandleAsync(orderId);

        // The same message arrives again later, for example after a crash before the ack.
        _time.Now = firstTime.AddMinutes(5);
        FulfilmentResult second;
        await using (var db = new ShelfDbContext(_options))
            second = await NewHandler(db).HandleAsync(orderId);

        await using var check = new ShelfDbContext(_options);
        var order = await check.Orders.SingleAsync(o => o.Id == orderId);
        Assert.Multiple(async () =>
        {
            Assert.That(second, Is.EqualTo(FulfilmentResult.AlreadyProcessed));
            Assert.That(await check.ProcessedMessages.CountAsync(), Is.EqualTo(1));
            Assert.That(order.FulfilledAtUtc, Is.EqualTo(firstTime.UtcDateTime), "the second delivery must not move the fulfilment time");
        });
    }

    [Test]
    public async Task Message_for_an_unknown_order_throws_so_retry_and_the_error_queue_take_over()
    {
        await using var db = new ShelfDbContext(_options);
        var handler = NewHandler(db);

        Assert.ThrowsAsync<OrderNotFoundException>(() => handler.HandleAsync(Guid.NewGuid()));
        Assert.That(await db.ProcessedMessages.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Simulated_work_runs_before_the_order_is_marked_fulfilled()
    {
        var orderId = await SeedPlacedOrderAsync();
        await using var db = new ShelfDbContext(_options);
        var handler = new OrderFulfilment(db, _time, Options.Create(new FulfilmentOptions { SimulatedWorkMs = 300 }), NullLogger<OrderFulfilment>.Instance);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await handler.HandleAsync(orderId);

        Assert.That(result, Is.EqualTo(FulfilmentResult.Fulfilled));
        Assert.That(clock.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250)), "the configured work delay did not run");
    }
}
