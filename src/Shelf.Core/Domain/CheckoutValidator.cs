using Shelf.Core.Catalogue;
using Shelf.Core.Data;

namespace Shelf.Core.Domain;

public static class CheckoutValidator
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 10;

    /// <summary>Longest address the Orders.Email column holds. The DbContext uses the same constant.</summary>
    public const int MaxEmailLength = 320;

    public const string EmptyCart = "The cart is empty.";
    public const string InvalidEmail = "A valid email address is required.";

    public static string BadQuantity(string bookId) => $"Quantity for '{bookId}' must be between {MinQuantity} and {MaxQuantity}.";

    public static string UnknownBook(string bookId) => $"Book '{bookId}' is not in the catalogue.";

    /// <summary>Returns every problem with the checkout, not just the first, so the storefront can show them all. Empty means valid.</summary>
    public static IReadOnlyList<string> Validate(string? email, IReadOnlyCollection<CartItem> items, IReadOnlyDictionary<string, Book> books)
    {
        var errors = new List<string>();

        if (!IsPlausibleEmail(email))
            errors.Add(InvalidEmail);

        if (items.Count == 0)
        {
            errors.Add(EmptyCart);
            return errors;
        }

        foreach (var item in items)
        {
            if (item.Quantity < MinQuantity || item.Quantity > MaxQuantity)
                errors.Add(BadQuantity(item.BookId));
            if (!books.ContainsKey(item.BookId))
                errors.Add(UnknownBook(item.BookId));
        }

        return errors;
    }

    /// <summary>Deliberately loose: one @ with something on both sides and no spaces. Real verification is a confirmation email.</summary>
    public static bool IsPlausibleEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;
        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        return trimmed.Length <= MaxEmailLength
            && at > 0
            && at < trimmed.Length - 1
            && trimmed.IndexOf('@', at + 1) < 0
            && !trimmed.Any(char.IsWhiteSpace);
    }
}
