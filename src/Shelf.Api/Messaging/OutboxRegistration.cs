using MassTransit;
using Shelf.Core.Data;

namespace Shelf.Api.Messaging;

public static class OutboxRegistration
{
    /// <summary>
    /// Transactional outbox: IPublishEndpoint inside a request writes to the OutboxMessage table through the
    /// request's ShelfDbContext, so the message commits or rolls back with the order. A hosted service reads the
    /// table and sends to the broker, and keeps retrying while the broker is down.
    /// Program.cs and the integration tests both call this. The tests must: MassTransit's test harness removes the
    /// app's MassTransit registrations, the outbox included, before it adds its in-memory bus.
    /// </summary>
    public static IBusRegistrationConfigurator AddShelfOutbox(this IBusRegistrationConfigurator x, TimeSpan queryDelay)
    {
        x.AddEntityFrameworkOutbox<ShelfDbContext>(o =>
        {
            o.UseSqlServer();
            o.UseBusOutbox();
            o.QueryDelay = queryDelay;
        });
        return x;
    }
}
