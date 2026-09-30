using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shelf.Core.Data;
using Shelf.Fulfilment;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<FulfilmentOptions>(builder.Configuration.GetSection("Fulfilment"));
builder.Services.AddSingleton(TimeProvider.System);

// Same database as the API. The worker never runs migrations; the API owns the schema.
builder.Services.AddDbContext<ShelfDbContext>((sp, options) =>
    options.UseSqlServer(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Orders"),
        sql => sql.EnableRetryOnFailure()));

builder.Services.AddScoped<IOrderFulfilment, OrderFulfilment>();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddConsumer<OrderPlacedConsumer, OrderPlacedConsumerDefinition>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var mq = context.GetRequiredService<IConfiguration>().GetSection("RabbitMq");
        cfg.Host(mq["Host"] ?? "localhost", "/", h =>
        {
            h.Username(mq["Username"] ?? "guest");
            h.Password(mq["Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

builder.Build().Run();
