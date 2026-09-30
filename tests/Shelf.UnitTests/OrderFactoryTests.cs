using Shelf.Core.Catalogue;
using Shelf.Core.Data;
using Shelf.Core.Domain;

namespace Shelf.UnitTests;

public class OrderFactoryTests
{
    private static readonly IReadOnlyDictionary<string, Book> Books = new Dictionary<string, Book>
    {
        ["emma"] = new("emma", "Emma", "Jane Austen", 1815, 11.50m),
        ["dracula"] = new("dracula", "Dracula", "Bram Stoker", 1897, 7.99m),
    };

    private static readonly DateTime PlacedAt = new(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Order_is_placed_with_catalogue_prices_and_the_sum_as_total()
    {
        var id = Guid.NewGuid();
        var items = new[]
        {
            new CartItem { BookId = "emma", Quantity = 2 },
            new CartItem { BookId = "dracula", Quantity = 1 },
        };

        var order = OrderFactory.Create(id, " reader@example.com ", items, Books, PlacedAt);

        Assert.Multiple(() =>
        {
            Assert.That(order.Id, Is.EqualTo(id));
            Assert.That(order.Status, Is.EqualTo(OrderStatus.Placed));
            Assert.That(order.Email, Is.EqualTo("reader@example.com"));
            Assert.That(order.PlacedAtUtc, Is.EqualTo(PlacedAt));
            Assert.That(order.FulfilledAtUtc, Is.Null);
            Assert.That(order.Total, Is.EqualTo(2 * 11.50m + 7.99m));
            Assert.That(order.Lines.Select(l => (l.BookId, l.Title, l.UnitPrice, l.Quantity)), Is.EqualTo(new[]
            {
                ("emma", "Emma", 11.50m, 2),
                ("dracula", "Dracula", 7.99m, 1),
            }));
            Assert.That(order.Lines.All(l => l.OrderId == id), Is.True);
        });
    }

    [Test]
    public void Later_catalogue_price_change_does_not_touch_an_existing_order()
    {
        var catalogue = new Dictionary<string, Book> { ["emma"] = new("emma", "Emma", "Jane Austen", 1815, 11.50m) };
        var order = OrderFactory.Create(Guid.NewGuid(), "reader@example.com", new[] { new CartItem { BookId = "emma", Quantity = 1 } }, catalogue, PlacedAt);

        catalogue["emma"] = catalogue["emma"] with { Price = 99.00m };

        Assert.That(order.Lines.Single().UnitPrice, Is.EqualTo(11.50m));
        Assert.That(order.Total, Is.EqualTo(11.50m));
    }
}
