using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelf.Api;
using Shelf.Contracts;
using Shelf.Core.Data;

namespace Shelf.IntegrationTests;

public class CheckoutApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ShelfApiFactory _factory = null!;
    private ITestHarness _harness = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ShelfApiFactory();
        _factory.CreateClient().Dispose(); // starts the host, which runs the migrations
        _harness = _factory.Services.GetTestHarness();
    }

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    private HttpClient NewCartClient(out Guid cartId)
    {
        cartId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CartHeader.Name, cartId.ToString());
        return client;
    }

    private async Task<T> WithDb<T>(Func<ShelfDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ShelfDbContext>());
    }

    [Test]
    public async Task Committed_migrations_are_applied_to_SQL_Server_at_startup()
    {
        var applied = await WithDb(async db => (await db.Database.GetAppliedMigrationsAsync()).ToList());
        var pending = await WithDb(async db => (await db.Database.GetPendingMigrationsAsync()).ToList());

        Assert.That(applied, Has.Some.EndsWith("_InitialCreate"));
        Assert.That(pending, Is.Empty);
    }

    [Test]
    public async Task Checkout_saves_the_order_with_catalogue_prices_empties_the_cart_and_publishes_OrderPlaced()
    {
        using var client = NewCartClient(out var cartId);
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "pride-and-prejudice", quantity = 2 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "dracula", quantity = 1 })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/checkout", new { email = "reader@example.com" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var body = (await response.Content.ReadFromJsonAsync<CheckoutResponse>(Json))!;
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/api/orders/{body.OrderId}"));
        Assert.That(body.Status, Is.EqualTo(OrderStatus.Placed));
        Assert.That(body.Total, Is.EqualTo(2 * 12.99m + 7.99m));

        // The row in SQL Server, read back through a fresh DbContext.
        var order = await WithDb(db => db.Orders.Include(o => o.Lines).SingleAsync(o => o.Id == body.OrderId));
        Assert.Multiple(() =>
        {
            Assert.That(order.Email, Is.EqualTo("reader@example.com"));
            Assert.That(order.Status, Is.EqualTo(OrderStatus.Placed));
            Assert.That(order.Total, Is.EqualTo(33.97m), "decimal(10,2) must round-trip exactly");
            Assert.That(order.Lines.OrderBy(l => l.BookId).Select(l => (l.BookId, l.Title, l.UnitPrice, l.Quantity)), Is.EqualTo(new[]
            {
                ("dracula", "Dracula", 7.99m, 1),
                ("pride-and-prejudice", "Pride and Prejudice", 12.99m, 2),
            }));
        });

        var cartItemsLeft = await WithDb(db => db.CartItems.CountAsync(i => i.CartId == cartId));
        Assert.That(cartItemsLeft, Is.EqualTo(0), "the order and the emptied cart commit together");

        Assert.That(await _harness.Published.Any<OrderPlaced>(m => m.Context.Message.OrderId == body.OrderId && m.Context.Message.Total == 33.97m), Is.True);
    }

    [Test]
    public async Task Price_sent_by_the_client_is_ignored()
    {
        using var client = NewCartClient(out _);
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 1, price = 0.01m, unitPrice = 0.01m })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/checkout", new { email = "reader@example.com", total = 0.01m });
        var body = (await response.Content.ReadFromJsonAsync<CheckoutResponse>(Json))!;

        Assert.That(body.Total, Is.EqualTo(11.50m));
        var line = await WithDb(db => db.OrderLines.SingleAsync(l => l.OrderId == body.OrderId));
        Assert.That(line.UnitPrice, Is.EqualTo(11.50m));
    }

    [Test]
    public async Task Checkout_with_an_empty_cart_is_400_and_writes_no_order()
    {
        using var client = NewCartClient(out _);
        var email = $"empty-{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync("/api/checkout", new { email });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("The cart is empty."));
        Assert.That(await WithDb(db => db.Orders.CountAsync(o => o.Email == email)), Is.EqualTo(0));
    }

    [Test]
    public async Task Checkout_with_a_bad_email_is_400_and_the_cart_is_kept()
    {
        using var client = NewCartClient(out var cartId);
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 1 })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/checkout", new { email = "not-an-email" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await WithDb(db => db.CartItems.CountAsync(i => i.CartId == cartId)), Is.EqualTo(1));
    }

    [Test]
    public async Task Adding_the_same_book_twice_increments_the_quantity_and_the_cart_is_priced_from_the_catalogue()
    {
        using var client = NewCartClient(out _);
        await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 1 });
        var response = await client.PostAsJsonAsync("/api/cart/items", new { bookId = "EMMA", quantity = 2 });

        var cart = (await response.Content.ReadFromJsonAsync<CartView>(Json))!;
        Assert.That(cart.Items.Select(i => (i.BookId, i.Quantity, i.UnitPrice, i.LineTotal)), Is.EqualTo(new[] { ("emma", 3, 11.50m, 34.50m) }));
        Assert.That(cart.Total, Is.EqualTo(34.50m));
    }

    [Test]
    public async Task Unknown_book_and_over_limit_quantity_are_rejected_when_adding()
    {
        using var client = NewCartClient(out _);

        var unknown = await client.PostAsJsonAsync("/api/cart/items", new { bookId = "ulysses", quantity = 1 });
        await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 9 });
        var overLimit = await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 2 });

        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(overLimit.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Cart_calls_without_a_cart_id_header_are_400()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/cart");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Placed_order_can_be_read_back_and_an_unknown_order_is_404()
    {
        using var client = NewCartClient(out _);
        await client.PostAsJsonAsync("/api/cart/items", new { bookId = "dracula", quantity = 1 });
        var placed = (await (await client.PostAsJsonAsync("/api/checkout", new { email = "reader@example.com" })).Content.ReadFromJsonAsync<CheckoutResponse>(Json))!;

        var order = await client.GetFromJsonAsync<OrderView>($"/api/orders/{placed.OrderId}", Json);
        var missing = await client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.That(order!.Status, Is.EqualTo(OrderStatus.Placed));
        Assert.That(order.Lines.Single().Title, Is.EqualTo("Dracula"));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Books_endpoint_serves_the_catalogue()
    {
        using var client = _factory.CreateClient();

        var books = await client.GetFromJsonAsync<List<Shelf.Core.Catalogue.Book>>("/api/books", Json);
        var missing = await client.GetAsync("/api/books/ulysses");

        Assert.That(books!.Select(b => b.Slug), Is.EquivalentTo(new[] { "emma", "dracula", "pride-and-prejudice" }));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
