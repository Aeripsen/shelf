using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Raven.Client.Documents;
using Shelf.Api.Catalogue;
using Shelf.Api.Startup;
using Shelf.Contracts;
using Shelf.Core.Catalogue;
using Shelf.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System);

// Carts, orders and the outbox: EF Core over SQL Server. The connection string is read when the first DbContext is
// built, so the integration tests can point it at their own container. Any IInterceptor in the container is added
// too; nothing registers one in production, and the integration tests use it to inject a lost commit acknowledgement.
builder.Services.AddDbContext<ShelfDbContext>((sp, options) => options
    .UseSqlServer(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Orders"),
        sql => sql.EnableRetryOnFailure())
    .AddInterceptors(sp.GetServices<IInterceptor>()));

// Catalogue: RavenDB. The store is created on first use, so tests that fake ICatalogue never connect to it.
builder.Services.AddSingleton<IDocumentStore>(sp => RavenStore.Create(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<ICatalogue>(sp => new RavenCatalogue(sp.GetRequiredService<IDocumentStore>()));

// Startup work runs in registration order before the API takes requests: migrate SQL Server, then seed RavenDB
// and deploy its index.
builder.Services.AddHostedService<DatabaseMigrator>();
builder.Services.AddHostedService<CatalogueSeeder>();

// /health reports on the three things checkout needs. MassTransit adds its own bus check (RabbitMQ connection).
builder.Services.AddHealthChecks()
    .AddCheck<SqlServerHealthCheck>(SqlServerHealthCheck.Name)
    .AddCheck<RavenHealthCheck>(RavenHealthCheck.Name);

builder.Services.AddMassTransit(x =>
{
    // Transactional outbox: IPublishEndpoint inside a request writes to the OutboxMessage table through the
    // request's ShelfDbContext, so the message commits or rolls back with the order. A hosted service reads the
    // table and sends to RabbitMQ, and keeps retrying if the broker is down.
    x.AddEntityFrameworkOutbox<ShelfDbContext>(o =>
    {
        o.UseSqlServer();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromMilliseconds(builder.Configuration.GetValue("Outbox:QueryDelayMs", 1000));
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var mq = context.GetRequiredService<IConfiguration>().GetSection("RabbitMq");
        cfg.Host(mq["Host"] ?? "localhost", "/", h =>
        {
            h.Username(mq["Username"] ?? "guest");
            h.Password(mq["Password"] ?? "guest");
        });

        // RabbitMQ drops a message published to an exchange with no queue bound. The worker's queue normally
        // appears only when the worker first starts, so the API declares it too and binds it to the OrderPlaced
        // exchange. The worker's own binding goes through a second exchange; RabbitMQ still puts a message in a
        // queue at most once, however many bindings lead there.
        // DeployPublishTopology does this when the bus starts. It has to: the outbox delivery service sends each
        // stored message to its exchange through a send endpoint, which never applies publish-side bindings.
        cfg.Publish<OrderPlaced>(p => p.BindQueue(p.Exchange.ExchangeName, QueueNames.OrderPlaced));
        cfg.DeployPublishTopology = true;
    });
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Swagger stays on in every environment: this is a demo service and the UI is the easiest way to try the API.
app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Lets the integration tests start the API in memory with WebApplicationFactory<Program>.
public partial class Program { }
