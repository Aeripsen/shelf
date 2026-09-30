using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Raven.Client.Documents;
using Shelf.Api.Catalogue;
using Shelf.Api.Startup;

namespace Shelf.IntegrationTests;

/// <summary>
/// The RavenDB catalogue against a real RavenDB 7.2 server in a container: the seed, the id-prefix listing and its
/// paging loop, batch loads, the Books/Search index, and the health check. Each test uses its own database.
/// </summary>
public class RavenCatalogueTests
{
    private IContainer _raven = null!;
    private string _url = "";

    [OneTimeSetUp]
    public async Task StartRavenDb()
    {
        var external = Environment.GetEnvironmentVariable("SHELF_TEST_RAVEN");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _url = external;
            return;
        }

        // Same image and settings as docker-compose.yml.
        _raven = new ContainerBuilder("ravendb/ravendb:7.2-ubuntu-latest")
            .WithEnvironment("RAVEN_Setup_Mode", "None")
            .WithEnvironment("RAVEN_License_Eula_Accepted", "true")
            .WithEnvironment("RAVEN_Security_UnsecuredAccessAllowed", "PublicNetwork")
            .WithEnvironment("RAVEN_ServerUrl", "http://0.0.0.0:8080")
            .WithPortBinding(8080, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/build/version")))
            .Build();
        await _raven.StartAsync();
        _url = $"http://{_raven.Hostname}:{_raven.GetMappedPublicPort(8080)}";
    }

    [OneTimeTearDown]
    public async Task StopRavenDb()
    {
        if (_raven is not null)
            await _raven.DisposeAsync();
    }

    /// <summary>A store built by the API's own RavenStore.Create, pointed at a fresh database.</summary>
    private IDocumentStore NewStore() => RavenStore.Create(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RavenDb:Url"] = _url,
            ["RavenDb:Database"] = $"ShelfTest_{Guid.NewGuid():N}",
        })
        .Build());

    private async Task<IDocumentStore> SeededStore()
    {
        var store = NewStore();
        await CatalogueSeeder.SeedAsync(store, CatalogueSeeder.LoadSeed());
        return store;
    }

    [Test]
    public async Task Seed_creates_the_database_and_running_it_twice_gives_the_same_catalogue()
    {
        using var store = NewStore();
        var seed = CatalogueSeeder.LoadSeed();

        await CatalogueSeeder.SeedAsync(store, seed);
        await CatalogueSeeder.SeedAsync(store, seed);

        var books = await new RavenCatalogue(store).GetAllAsync();
        Assert.That(seed, Has.Count.EqualTo(10));
        Assert.That(books.Select(b => b.Slug), Is.EquivalentTo(seed.Select(d => d.Id["books/".Length..])));
        Assert.That(books.Select(b => b.Title), Is.Ordered.Using((IComparer<string>)StringComparer.OrdinalIgnoreCase));
    }

    [Test]
    public async Task Listing_reads_the_catalogue_in_pages_and_misses_nothing()
    {
        using var store = await SeededStore();

        // Ten books with three per round trip is four pages, the last one short. One page alone would return three.
        var paged = await new RavenCatalogue(store, pageSize: 3).GetAllAsync();
        var exact = await new RavenCatalogue(store, pageSize: 5).GetAllAsync();
        var single = await new RavenCatalogue(store).GetAllAsync();

        Assert.That(paged, Has.Count.EqualTo(10));
        Assert.That(paged, Is.EqualTo(single));
        Assert.That(exact, Is.EqualTo(single), "a full last page is followed by an empty one, not a missed book");
    }

    [Test]
    public async Task Books_are_loaded_by_slug_one_or_many_at_a_time()
    {
        using var store = await SeededStore();
        var catalogue = new RavenCatalogue(store);

        var one = await catalogue.GetAsync("dracula");
        var missing = await catalogue.GetAsync("ulysses");
        var many = await catalogue.GetManyAsync(new[] { "emma", "ulysses", "moby-dick", "emma" });

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.EqualTo(new Shelf.Core.Catalogue.Book("dracula", "Dracula", "Bram Stoker", 1897, 7.99m)));
            Assert.That(missing, Is.Null);
            Assert.That(many.Keys, Is.EquivalentTo(new[] { "emma", "moby-dick" }));
            Assert.That(many["MOBY-DICK"].Price, Is.EqualTo(15.25m), "keys ignore case");
        });
    }

    [TestCase("austen", new[] { "emma", "pride-and-prejudice" })]
    [TestCase("AUST", new[] { "emma", "pride-and-prejudice" })]
    [TestCase("austen pride", new[] { "pride-and-prejudice" })]
    [TestCase("bronte", new[] { "jane-eyre", "wuthering-heights" })]
    [TestCase("tale", new[] { "a-tale-of-two-cities" })]
    [TestCase("ulysses", new string[0])]
    [TestCase("*:(\") OR", new string[0])]
    public async Task Search_uses_the_full_text_index_over_title_and_author(string text, string[] expected)
    {
        using var store = await SeededStore();

        var found = await new RavenCatalogue(store).SearchAsync(text);

        Assert.That(found.Select(b => b.Slug), Is.EquivalentTo(expected));
    }

    [Test]
    public async Task Health_check_is_healthy_against_a_running_server()
    {
        using var store = await SeededStore();

        var result = await new RavenHealthCheck(store).CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
        Assert.That(result.Description, Does.StartWith("10 documents"));
    }
}
