using Shelf.Core.Catalogue;
using Shelf.Core.Data;

namespace Shelf.Core.Domain;

public static class OrderFactory
{
    /// <summary>
    /// Builds a Placed order from a cart that already passed CheckoutValidator. Title and unit price come from the
    /// catalogue, never from the client, and are copied onto the order lines.
    /// </summary>
    public static Order Create(Guid orderId, string email, IEnumerable<CartItem> items, IReadOnlyDictionary<string, Book> books, DateTime placedAtUtc)
    {
        var lines = items
            .Select(i => new PricedLine(i.BookId, books[i.BookId].Title, books[i.BookId].Price, i.Quantity))
            .ToList();

        return new Order
        {
            Id = orderId,
            Email = email.Trim(),
            Status = OrderStatus.Placed,
            PlacedAtUtc = placedAtUtc,
            Total = CartPricing.Total(lines),
            Lines = lines.Select(l => new OrderLine
            {
                OrderId = orderId,
                BookId = l.BookId,
                Title = l.Title,
                UnitPrice = l.UnitPrice,
                Quantity = l.Quantity,
            }).ToList(),
        };
    }
}
