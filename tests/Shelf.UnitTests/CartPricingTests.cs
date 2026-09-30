using Shelf.Core.Domain;

namespace Shelf.UnitTests;

public class CartPricingTests
{
    [Test]
    public void Total_adds_unit_price_times_quantity_across_lines()
    {
        var lines = new[]
        {
            new PricedLine("pride-and-prejudice", "Pride and Prejudice", 12.99m, 3),
            new PricedLine("emma", "Emma", 5.50m, 1),
        };

        Assert.That(CartPricing.Total(lines), Is.EqualTo(44.47m));
    }

    [Test]
    public void Three_copies_at_12_99_is_exactly_38_97()
    {
        var lines = new[] { new PricedLine("pride-and-prejudice", "Pride and Prejudice", 12.99m, 3) };

        Assert.That(CartPricing.Total(lines), Is.EqualTo(38.97m));
    }

    [Test]
    public void Ten_lines_of_ten_cents_is_exactly_one_dollar()
    {
        // With double this would be 0.9999999999999999; money is decimal for this reason.
        var lines = Enumerable.Range(0, 10).Select(i => new PricedLine($"b{i}", $"Book {i}", 0.10m, 1));

        Assert.That(CartPricing.Total(lines), Is.EqualTo(1.00m));
    }

    [Test]
    public void Empty_cart_totals_zero()
    {
        Assert.That(CartPricing.Total(Array.Empty<PricedLine>()), Is.EqualTo(0m));
    }

    [Test]
    public void Line_total_is_unit_price_times_quantity()
    {
        Assert.That(new PricedLine("x", "X", 7.99m, 4).LineTotal, Is.EqualTo(31.96m));
    }
}
