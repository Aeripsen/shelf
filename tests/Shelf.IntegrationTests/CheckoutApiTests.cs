using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shelf.Api;
using Shelf.Contracts;
using Shelf.Core.Catalogue;
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
        _factory = new ShelfApiFactory("ShelfApiTests");
        _factory.CreateClient().Dispose(); // starts the host, which runs the migrations
        _harness = _factory.Services.GetTestHarness();
    }

    [OneTimeTearDown]
    public void StopApi() => _factory.Dispose();

    [TearDown]
    public void Disarm() => _factory.Catalogue.GetManyBarrier = null;

    private HttpClient NewCartClient(out Guid cartId)
    {
        cartId = Guid.NewGuid();
        return CartClient(cartId);
    }

    private HttpClient CartClient(Guid cartId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(CartHeader.Name, cartId.ToString());
        return client;
    }

    private async Task<T> WithDb<T>(Func<ShelfDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<ShelfDbContext>());
    }

    private static string UniqueEmail() => $"reader-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// True once the outbox delivery service has sent OrderPlaced for this order and the recorder consumed it.
    /// The harness's Published list is not used: it also records a publish that was only written to the outbox and
    /// then rolled back.
    /// </summary>
    private async Task<bool> OrderPlacedWasDelivered(Guid orderId) =>
        await _harness.GetConsumerHarness<OrderPlacedRecorder>().Consumed.Any<OrderPlaced>(m => m.Context.Message.OrderId == orderId);

    private List<Guid> DeliveredSoFar() =>
        _harness.GetConsumerHarness<OrderPlacedRecorder>().Consumed
            .Select<OrderPlaced>(new CancellationToken(canceled: true))
            .Select(m => m.Context.Message.OrderId)
            .ToList();

    [Test]
    public async Task Checkout_saves_the_order_with_catalogue_prices_empties_the_cart_and_the_outbox_sends_OrderPlaced()
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
        Assert.That(cartItemsLeft, Is.EqualTo(0), "checkout empties the cart");

        // Sent by the outbox delivery service after the commit, not by the request itself.
        Assert.That(await _harness.GetConsumerHarness<OrderPlacedRecorder>().Consumed
            .Any<OrderPlaced>(m => m.Context.Message.OrderId == body.OrderId && m.Context.Message.Total == 33.97m), Is.True);
    }

    [Test]
    public async Task A_publish_whose_transaction_never_commits_is_never_delivered_and_one_that_commits_is()
    {
        // The outbox itself, without the API around it: the same scoped IPublishEndpoint and ShelfDbContext the
        // checkout controller gets. Without the outbox, the first publish would reach the bus at once.
        var discarded = Guid.NewGuid();
        var saved = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var publish = scope.ServiceProvider.GetRequiredService<MassTransit.IPublishEndpoint>();
            await publish.Publish(new OrderPlaced(discarded, 1m, DateTime.UtcNow));
            // No SaveChanges: the scope ends and the outbox row is never written.
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var publish = scope.ServiceProvider.GetRequiredService<MassTransit.IPublishEndpoint>();
            await publish.Publish(new OrderPlaced(saved, 1m, DateTime.UtcNow));
            await scope.ServiceProvider.GetRequiredService<ShelfDbContext>().SaveChangesAsync();
        }

        Assert.That(await OrderPlacedWasDelivered(saved), Is.True, "the committed publish was never delivered");
        await Task.Delay(TimeSpan.FromSeconds(1)); // ten outbox polls
        Assert.That(DeliveredSoFar(), Does.Not.Contain(discarded));
    }

    [Test]
    public async Task Two_concurrent_checkouts_of_one_cart_give_one_201_one_409_and_one_order()
    {
        using var client = NewCartClient(out var cartId);
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 2 })).EnsureSuccessStatusCode();
        var email = UniqueEmail();

        // Both requests read the cart, then meet in the catalogue call, then both try to commit. The first commit
        // deletes the cart lines; the second finds its rowversions gone, so its whole transaction rolls back.
        _factory.Catalogue.GetManyBarrier = new Barrier(2);
        using var second = CartClient(cartId);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/checkout", new { email }),
            second.PostAsJsonAsync("/api/checkout", new { email }));
        _factory.Catalogue.GetManyBarrier = null;

        var codes = responses.Select(r => r.StatusCode).OrderBy(c => c).ToList();
        Assert.That(codes, Is.EqualTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));

        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.That(await conflict.Content.ReadAsStringAsync(), Does.Contain("Your cart changed"));

        var orders = await WithDb(db => db.Orders.Where(o => o.Email == email).Select(o => o.Id).ToListAsync());
        Assert.That(orders, Has.Count.EqualTo(1), "the losing checkout's order must roll back with its failed delete");
        Assert.That(await OrderPlacedWasDelivered(orders[0]), Is.True);

        // The loser also wrote an OrderPlaced to the outbox before its transaction failed. Give the delivery service
        // ten polls, then check that every order it has delivered an event for in this fixture is committed.
        // Delivery is at least once, so the same order may appear twice; that is allowed, a missing order is not.
        await Task.Delay(TimeSpan.FromSeconds(1));
        var delivered = DeliveredSoFar();
        var distinct = delivered.Distinct().ToList();
        var committed = await WithDb(db => db.Orders.Where(o => distinct.Contains(o.Id)).Select(o => o.Id).ToListAsync());
        TestContext.Progress.WriteLine($"OrderPlaced deliveries so far: {delivered.Count}, distinct orders: {distinct.Count}, " +
            $"repeated: {string.Join(", ", delivered.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => $"{g.Key} x{g.Count()}"))}");
        Assert.That(distinct.Except(committed), Is.Empty, "an event was delivered for an order that rolled back");
    }

    [Test]
    public async Task Lost_commit_acknowledgement_is_not_replayed_into_a_conflict()
    {
        // SQL Server commits the order, then the client sees a timeout and EF Core's retry strategy kicks in. A blind
        // replay would try to delete cart lines that are already gone and answer 409 for an order that exists.
        // verifySucceeded finds the committed order and the API answers 201 instead.
        using var client = NewCartClient(out var cartId);
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "dracula", quantity = 1 })).EnsureSuccessStatusCode();
        var email = UniqueEmail();
        var firedBefore = _factory.LostAck.TimesFired;

        _factory.LostAck.Arm();
        var response = await client.PostAsJsonAsync("/api/checkout", new { email });

        Assert.That(_factory.LostAck.TimesFired, Is.EqualTo(firedBefore + 1), "the fault was never injected");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var body = (await response.Content.ReadFromJsonAsync<CheckoutResponse>(Json))!;

        var orders = await WithDb(db => db.Orders.Where(o => o.Email == email).Select(o => o.Id).ToListAsync());
        Assert.That(orders, Is.EqualTo(new[] { body.OrderId }));
        Assert.That(await WithDb(db => db.CartItems.CountAsync(i => i.CartId == cartId)), Is.EqualTo(0));
        Assert.That(await OrderPlacedWasDelivered(body.OrderId), Is.True, "the event committed with the order, so the outbox sends it");
    }

    [Test]
    public async Task Price_fields_in_requests_are_not_part_of_the_API_and_have_no_effect()
    {
        // The request types have no price field at all, so this guards against one being added later: the
        // serializer drops the extra properties and the order is priced from the catalogue.
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
        var email = UniqueEmail();

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
    public async Task Concurrent_adds_to_a_new_cart_are_all_kept()
    {
        // Five simultaneous "add one copy" calls on a cart that does not exist yet. They race to create the cart,
        // to insert the line, and to bump its quantity; the retry on rowversion and key conflicts must keep all five.
        using var client = NewCartClient(out var cartId);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 1 })));

        Assert.That(responses.Select(r => r.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
        var quantity = await WithDb(db => db.CartItems.Where(i => i.CartId == cartId).Select(i => i.Quantity).SingleAsync());
        Assert.That(quantity, Is.EqualTo(5));
    }

    [Test]
    public async Task Removing_a_line_deletes_only_that_book_and_removing_a_missing_book_changes_nothing()
    {
        using var client = NewCartClient(out var cartId);
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "emma", quantity = 2 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/cart/items", new { bookId = "dracula", quantity = 1 })).EnsureSuccessStatusCode();

        var removed = await client.DeleteAsync("/api/cart/items/emma");
        var cart = (await removed.Content.ReadFromJsonAsync<CartView>(Json))!;
        var again = await client.DeleteAsync("/api/cart/items/emma");
        var unchanged = (await again.Content.ReadFromJsonAsync<CartView>(Json))!;

        Assert.Multiple(async () =>
        {
            Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(cart.Items.Select(i => (i.BookId, i.Quantity)), Is.EqualTo(new[] { ("dracula", 1) }));
            Assert.That(cart.Total, Is.EqualTo(7.99m));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(unchanged.Items.Select(i => i.BookId), Is.EqualTo(new[] { "dracula" }));
            Assert.That(await WithDb(db => db.CartItems.Where(i => i.CartId == cartId).Select(i => i.BookId).ToListAsync()), Is.EqualTo(new[] { "dracula" }));
        });
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

        var get = await client.GetAsync("/api/cart");
        var delete = await client.DeleteAsync("/api/cart/items/emma");

        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
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
    public async Task Books_endpoints_list_search_and_get_by_slug()
    {
        using var client = _factory.CreateClient();

        var all = await client.GetFromJsonAsync<List<Book>>("/api/books", Json);
        var search = await client.GetFromJsonAsync<List<Book>>("/api/books?q=austen", Json);
        var one = await client.GetFromJsonAsync<Book>("/api/books/dracula", Json);
        var missing = await client.GetAsync("/api/books/ulysses");

        Assert.That(all!.Select(b => b.Slug), Is.EqualTo(new[] { "dracula", "emma", "pride-and-prejudice" }));
        Assert.That(search!.Select(b => b.Slug), Is.EqualTo(new[] { "emma", "pride-and-prejudice" }));
        Assert.That(one, Is.EqualTo(ShelfApiFactory.Dracula));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Health_endpoint_checks_SQL_Server_and_the_bus()
    {
        using var client = _factory.CreateClient();

        var healthy = await client.GetAsync("/health");

        Assert.That(healthy.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await healthy.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
    }
}
