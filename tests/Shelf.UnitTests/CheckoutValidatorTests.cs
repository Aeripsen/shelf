using Shelf.Core.Catalogue;
using Shelf.Core.Data;
using Shelf.Core.Domain;

namespace Shelf.UnitTests;

public class CheckoutValidatorTests
{
    private static readonly IReadOnlyDictionary<string, Book> Books = new Dictionary<string, Book>
    {
        ["emma"] = new("emma", "Emma", "Jane Austen", 1815, 11.50m),
        ["dracula"] = new("dracula", "Dracula", "Bram Stoker", 1897, 7.99m),
    };

    private static CartItem Item(string bookId, int quantity) => new() { BookId = bookId, Quantity = quantity };

    [Test]
    public void Valid_cart_and_email_has_no_errors()
    {
        var errors = CheckoutValidator.Validate("reader@example.com", new[] { Item("emma", 2), Item("dracula", 1) }, Books);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Empty_cart_is_rejected()
    {
        var errors = CheckoutValidator.Validate("reader@example.com", Array.Empty<CartItem>(), Books);

        Assert.That(errors, Is.EqualTo(new[] { CheckoutValidator.EmptyCart }));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(11)]
    public void Quantity_outside_1_to_10_is_rejected(int quantity)
    {
        var errors = CheckoutValidator.Validate("reader@example.com", new[] { Item("emma", quantity) }, Books);

        Assert.That(errors, Is.EqualTo(new[] { CheckoutValidator.BadQuantity("emma") }));
    }

    [TestCase(1)]
    [TestCase(10)]
    public void Quantity_at_the_limits_is_accepted(int quantity)
    {
        var errors = CheckoutValidator.Validate("reader@example.com", new[] { Item("emma", quantity) }, Books);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void Book_missing_from_catalogue_is_rejected_by_name()
    {
        var errors = CheckoutValidator.Validate("reader@example.com", new[] { Item("emma", 1), Item("ulysses", 1) }, Books);

        Assert.That(errors, Is.EqualTo(new[] { CheckoutValidator.UnknownBook("ulysses") }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("no-at-sign")]
    [TestCase("@example.com")]
    [TestCase("reader@")]
    [TestCase("two@@example.com")]
    [TestCase("has space@example.com")]
    public void Implausible_email_is_rejected(string? email)
    {
        var errors = CheckoutValidator.Validate(email, new[] { Item("emma", 1) }, Books);

        Assert.That(errors, Is.EqualTo(new[] { CheckoutValidator.InvalidEmail }));
    }

    [Test]
    public void Email_at_the_column_limit_is_accepted_and_one_longer_is_rejected()
    {
        var atLimit = new string('a', CheckoutValidator.MaxEmailLength - "@example.com".Length) + "@example.com";
        var tooLong = "a" + atLimit;

        Assert.That(atLimit, Has.Length.EqualTo(320));
        Assert.That(CheckoutValidator.Validate(atLimit, new[] { Item("emma", 1) }, Books), Is.Empty);
        Assert.That(CheckoutValidator.Validate(tooLong, new[] { Item("emma", 1) }, Books), Is.EqualTo(new[] { CheckoutValidator.InvalidEmail }));
    }

    [Test]
    public void All_problems_are_reported_together()
    {
        var errors = CheckoutValidator.Validate("bad", new[] { Item("emma", 0), Item("ulysses", 1) }, Books);

        Assert.That(errors, Is.EquivalentTo(new[]
        {
            CheckoutValidator.InvalidEmail,
            CheckoutValidator.BadQuantity("emma"),
            CheckoutValidator.UnknownBook("ulysses"),
        }));
    }
}
