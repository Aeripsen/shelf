namespace Shelf.Core.Domain;

public record PricedLine(string BookId, string Title, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public static class CartPricing
{
    /// <summary>Sum of unit price times quantity. decimal, not double, so 3 x 12.99 is exactly 38.97.</summary>
    public static decimal Total(IEnumerable<PricedLine> lines) => lines.Sum(l => l.LineTotal);
}
