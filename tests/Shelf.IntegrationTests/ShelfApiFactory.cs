using System.Data.Common;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shelf.Api.Catalogue;
using Shelf.Core.Catalogue;

namespace Shelf.IntegrationTests;

/// <summary>
/// The real API, in memory, against the real SQL Server container, in its own database. Three things are swapped:
/// the RavenDB catalogue becomes a fixed three-book fake (RavenDB itself is tested in RavenCatalogueTests),
/// RabbitMQ becomes MassTransit's in-memory test harness, so the tests can see what the outbox delivered, and the
/// RavenDB health check is removed because there is no RavenDB here. Everything else is Program.cs as shipped,
/// including the transactional outbox and its delivery service.
/// </summary>
public class ShelfApiFactory(string database, bool migrate = true) : WebApplicationFactory<Program>
{
    public static readonly Book Emma = new("emma", "Emma", "Jane Austen", 1815, 11.50m);
    public static readonly Book Dracula = new("dracula", "Dracula", "Bram Stoker", 1897, 7.99m);
    public static readonly Book PrideAndPrejudice = new("pride-and-prejudice", "Pride and Prejudice", "Jane Austen", 1813, 12.99m);

    public string ConnectionString { get; } = SqlServerFixture.ConnectionStringFor(database);

    public FakeCatalogue Catalogue { get; } = new(Emma, Dracula, PrideAndPrejudice);

    public LostCommitAcknowledgement LostAck { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Orders"] = ConnectionString,
            ["Startup:SeedCatalogue"] = "false",
            ["Startup:MigrateDatabase"] = migrate ? "true" : "false",
            ["Outbox:QueryDelayMs"] = "100",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICatalogue>();
            services.AddSingleton<ICatalogue>(Catalogue);
            services.AddSingleton<IInterceptor>(LostAck);
            services.Configure<HealthCheckServiceOptions>(o =>
            {
                foreach (var raven in o.Registrations.Where(r => r.Name == RavenHealthCheck.Name).ToList())
                    o.Registrations.Remove(raven);
            });
            services.AddMassTransitTestHarness();
        });
    }
}

public class FakeCatalogue(params Book[] books) : ICatalogue
{
    private readonly Dictionary<string, Book> _books = books.ToDictionary(b => b.Slug, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When set, every GetManyAsync call waits here. Checkout calls GetManyAsync once, after it has read the cart and
    /// before it saves, so a barrier of two makes two checkouts both read the same cart before either one commits.
    /// </summary>
    public Barrier? GetManyBarrier { get; set; }

    public Task<IReadOnlyList<Book>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Book>>(_books.Values.OrderBy(b => b.Title).ToList());

    public Task<IReadOnlyList<Book>> SearchAsync(string text, CancellationToken ct = default)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult<IReadOnlyList<Book>>(_books.Values
            .Where(b => words.All(w => $"{b.Title} {b.Author}".Split(' ').Any(x => x.StartsWith(w, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(b => b.Title)
            .ToList());
    }

    public Task<Book?> GetAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(_books.GetValueOrDefault(slug));

    public Task<IReadOnlyDictionary<string, Book>> GetManyAsync(IEnumerable<string> slugs, CancellationToken ct = default)
    {
        var barrier = GetManyBarrier;
        if (barrier is not null && !barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("the other checkout never reached the catalogue");

        return Task.FromResult<IReadOnlyDictionary<string, Book>>(slugs
            .Where(_books.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(s => s, s => _books[s], StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Simulates a commit whose acknowledgement is lost: SQL Server commits, then the client sees a timeout, which EF
/// Core treats as transient and retries. Armed for one transaction that inserts an order; every other transaction
/// (cart writes, the outbox delivery service) passes through untouched.
/// </summary>
public sealed class LostCommitAcknowledgement : IDbCommandInterceptor, IDbTransactionInterceptor
{
    private readonly HashSet<DbTransaction> _insertingOrder = new(ReferenceEqualityComparer.Instance);
    private volatile bool _armed;
    private int _fired;

    public int TimesFired => _fired;

    public void Arm() => _armed = true;

    private void Note(DbCommand command)
    {
        if (_armed && command.Transaction is not null && command.CommandText.Contains("INSERT INTO [Orders]", StringComparison.Ordinal))
            lock (_insertingOrder)
                _insertingOrder.Add(command.Transaction);
    }

    public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Note(command);
        return ValueTask.FromResult(result);
    }

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Note(command);
        return ValueTask.FromResult(result);
    }

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        bool hit;
        lock (_insertingOrder)
            hit = _insertingOrder.Remove(transaction);

        if (hit && _armed)
        {
            _armed = false;
            Interlocked.Increment(ref _fired);
            throw new TimeoutException("simulated: the commit succeeded but its acknowledgement never arrived");
        }

        return Task.CompletedTask;
    }
}
