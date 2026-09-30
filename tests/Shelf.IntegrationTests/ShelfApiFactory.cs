using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shelf.Core.Catalogue;

namespace Shelf.IntegrationTests;

/// <summary>
/// The real API, in memory, against the real SQL Server container. Two things are swapped:
/// the RavenDB catalogue becomes a fixed three-book fake, and RabbitMQ becomes MassTransit's in-memory test harness,
/// so the tests can assert that OrderPlaced was published.
/// </summary>
public class ShelfApiFactory : WebApplicationFactory<Program>
{
    public static readonly Book Emma = new("emma", "Emma", "Jane Austen", 1815, 11.50m);
    public static readonly Book Dracula = new("dracula", "Dracula", "Bram Stoker", 1897, 7.99m);
    public static readonly Book PrideAndPrejudice = new("pride-and-prejudice", "Pride and Prejudice", "Jane Austen", 1813, 12.99m);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Orders"] = SqlServerFixture.ConnectionString,
            ["Startup:SeedCatalogue"] = "false",
            ["Startup:MigrateDatabase"] = "true",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICatalogue>();
            services.AddSingleton<ICatalogue>(new FakeCatalogue(Emma, Dracula, PrideAndPrejudice));
            services.AddMassTransitTestHarness();
        });
    }
}

public class FakeCatalogue(params Book[] books) : ICatalogue
{
    private readonly Dictionary<string, Book> _books = books.ToDictionary(b => b.Slug, StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<Book>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Book>>(_books.Values.OrderBy(b => b.Title).ToList());

    public Task<Book?> GetAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(_books.GetValueOrDefault(slug));

    public Task<IReadOnlyDictionary<string, Book>> GetManyAsync(IEnumerable<string> slugs, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, Book>>(slugs
            .Where(_books.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(s => s, s => _books[s], StringComparer.OrdinalIgnoreCase));
}
