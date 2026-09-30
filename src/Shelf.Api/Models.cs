using Shelf.Core.Data;

namespace Shelf.Api;

public record AddCartItemRequest(string BookId, int Quantity);

public record CartLineView(string BookId, string Title, decimal UnitPrice, int Quantity, decimal LineTotal, bool Available);

public record CartView(Guid CartId, IReadOnlyList<CartLineView> Items, decimal Total);

public record CheckoutRequest(string? Email);

public record CheckoutResponse(Guid OrderId, OrderStatus Status, decimal Total);

public record OrderLineView(string BookId, string Title, decimal UnitPrice, int Quantity);

public record OrderView(
    Guid OrderId,
    string Email,
    OrderStatus Status,
    decimal Total,
    DateTime PlacedAtUtc,
    DateTime? FulfilledAtUtc,
    IReadOnlyList<OrderLineView> Lines);

/// <summary>The storefront keeps a random GUID in localStorage and sends it on every cart and checkout call.</summary>
public static class CartHeader
{
    public const string Name = "X-Cart-Id";

    public static bool TryRead(HttpRequest request, out Guid cartId) =>
        Guid.TryParse(request.Headers[Name].ToString(), out cartId) && cartId != Guid.Empty;
}
