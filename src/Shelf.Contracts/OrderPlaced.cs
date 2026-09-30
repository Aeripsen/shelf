namespace Shelf.Contracts;

/// <summary>
/// Written to the outbox in the same transaction as the order, then sent to RabbitMQ by the API's outbox delivery
/// service. The fulfilment worker consumes it. Both services reference this one type because MassTransit routes by
/// the message type's full name.
/// </summary>
public record OrderPlaced(Guid OrderId, decimal Total, DateTime PlacedAtUtc);

public static class QueueNames
{
    /// <summary>
    /// The fulfilment worker's queue. The API also declares and binds it when it first publishes, so an order placed
    /// before the worker has ever started still waits in the queue instead of being dropped by the broker.
    /// </summary>
    public const string OrderPlaced = "order-placed";
}
