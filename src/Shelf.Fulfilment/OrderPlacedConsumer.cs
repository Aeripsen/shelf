using MassTransit;
using Shelf.Contracts;

namespace Shelf.Fulfilment;

public class OrderPlacedConsumer(IOrderFulfilment fulfilment, ILogger<OrderPlacedConsumer> log) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var orderId = context.Message.OrderId;
        var result = await fulfilment.HandleAsync(orderId, context.CancellationToken);
        log.LogInformation("Order {OrderId}: {Result} (retry attempt {Attempt})", orderId, result, context.GetRetryAttempt());
    }
}

/// <summary>
/// Endpoint and retry policy for the consumer. Kept in a definition so the worker and the tests use the same policy.
/// </summary>
public class OrderPlacedConsumerDefinition : ConsumerDefinition<OrderPlacedConsumer>
{
    public static readonly TimeSpan[] RetryIntervals =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    };

    public OrderPlacedConsumerDefinition()
    {
        EndpointName = "order-placed";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<OrderPlacedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // In-memory retries for transient faults such as a deadlock or a timeout. If every retry fails, MassTransit
        // moves the message to the order-placed_error queue, so one bad message cannot block the queue.
        endpointConfigurator.UseMessageRetry(r => r.Intervals(RetryIntervals));
    }
}
