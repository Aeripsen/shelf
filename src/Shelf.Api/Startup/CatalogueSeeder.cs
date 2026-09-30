using System.Text.Json;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Shelf.Api.Catalogue;

namespace Shelf.Api.Startup;

/// <summary>
/// Creates the RavenDB database if it is missing and stores every book from seed/books.json under its fixed id.
/// Storing by fixed id overwrites, so running the seed twice gives the same catalogue.
/// Turned off with Startup:SeedCatalogue=false (the integration tests do this and fake the catalogue instead).
/// </summary>
public class CatalogueSeeder(IServiceProvider services, IConfiguration config, ILogger<CatalogueSeeder> log) : IHostedService
{
    private const int MaxAttempts = 30;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!config.GetValue("Startup:SeedCatalogue", true))
        {
            log.LogInformation("Catalogue seed skipped (Startup:SeedCatalogue=false)");
            return;
        }

        var books = LoadSeed();
        var store = services.GetRequiredService<IDocumentStore>();

        // RavenDB can still be starting when the API starts, so retry for about a minute.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await EnsureDatabaseAsync(store, ct);
                using var session = store.OpenAsyncSession();
                foreach (var book in books)
                    await session.StoreAsync(book, book.Id, ct);
                await session.SaveChangesAsync(ct);
                log.LogInformation("Seeded {Count} books into RavenDB database {Database}", books.Count, store.Database);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
            {
                log.LogWarning("RavenDB not ready (attempt {Attempt}/{Max}): {Message}", attempt, MaxAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public static List<BookDocument> LoadSeed()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "seed", "books.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<BookDocument>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"No books in {path}");
    }

    private static async Task EnsureDatabaseAsync(IDocumentStore store, CancellationToken ct)
    {
        var record = await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(store.Database), ct);
        if (record is null)
            await store.Maintenance.Server.SendAsync(new CreateDatabaseOperation(new DatabaseRecord(store.Database)), ct);
    }
}
