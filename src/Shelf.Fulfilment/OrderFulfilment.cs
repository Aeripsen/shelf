using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shelf.Core.Data;

namespace Shelf.Fulfilment;

public enum FulfilmentResult
{
    Fulfilled,
    AlreadyProcessed,
}

public class FulfilmentOptions
{
    /// <summary>Stands in for slow downstream work (picking, shipping labels). 2000 ms in compose, 0 in tests.</summary>
    public int SimulatedWorkMs { get; set; }
}

public class OrderNotFoundException(Guid orderId) : Exception($"Order {orderId} does not exist.");

public interface IOrderFulfilment
{
    Task<FulfilmentResult> HandleAsync(Guid orderId, CancellationToken ct = default);
}

/// <summary>
/// Marks an order Fulfilled exactly once, however many times its OrderPlaced message is delivered.
/// RabbitMQ delivers at least once, so a copy can arrive again, for example after a crash before the ack.
/// </summary>
public class OrderFulfilment(
    ShelfDbContext db,
    TimeProvider time,
    IOptions<FulfilmentOptions> options,
    ILogger<OrderFulfilment> log) : IOrderFulfilment
{
    public async Task<FulfilmentResult> HandleAsync(Guid orderId, CancellationToken ct = default)
    {
        // Fast path: this message was handled before, so do nothing.
        if (await db.ProcessedMessages.AnyAsync(p => p.OrderId == orderId, ct))
            return FulfilmentResult.AlreadyProcessed;

        // Throwing lets the retry policy try again, and after the last retry the message goes to the error queue.
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        if (options.Value.SimulatedWorkMs > 0)
            await Task.Delay(options.Value.SimulatedWorkMs, ct);

        var now = time.GetUtcNow().UtcDateTime;
        order.Status = OrderStatus.Fulfilled;
        order.FulfilledAtUtc = now;
        db.ProcessedMessages.Add(new ProcessedMessage { OrderId = orderId, ProcessedAtUtc = now });

        try
        {
            // The status change and the ProcessedMessages row commit in one transaction.
            await db.SaveChangesAsync(ct);
            return FulfilmentResult.Fulfilled;
        }
        catch (DbUpdateException ex) when (SqlErrors.IsDuplicateKey(ex))
        {
            // Two copies raced past the check above. OrderId is the primary key of ProcessedMessages, so the second
            // insert fails, its whole transaction rolls back, and this copy counts as already processed.
            db.ChangeTracker.Clear();
            log.LogInformation("Order {OrderId}: duplicate delivery lost the race, treated as already processed", orderId);
            return FulfilmentResult.AlreadyProcessed;
        }
    }
}
