using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Raven.Client.Documents;
using Shelf.Api.Catalogue;
using Shelf.Api.Startup;
using Shelf.Core.Catalogue;
using Shelf.Core.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

// Carts and orders: EF Core over SQL Server. The connection string is read when the first DbContext is built,
// so the integration tests can point it at their own container.
builder.Services.AddDbContext<ShelfDbContext>((sp, options) =>
    options.UseSqlServer(
        sp.GetRequiredService<IConfiguration>().GetConnectionString("Orders"),
        sql => sql.EnableRetryOnFailure()));

// Catalogue: RavenDB. The store is created on first use, so tests that fake ICatalogue never connect to it.
builder.Services.AddSingleton<IDocumentStore>(sp => RavenStore.Create(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<ICatalogue, RavenCatalogue>();

// Startup work runs in registration order before the API takes requests: migrate SQL Server, then seed RavenDB.
builder.Services.AddHostedService<DatabaseMigrator>();
builder.Services.AddHostedService<CatalogueSeeder>();

// The API only publishes; it has no consumers.
builder.Services.AddMassTransit(x =>
{
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
