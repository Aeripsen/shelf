using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shelf.Core.Catalogue;
using Shelf.Core.Data;
using Shelf.Core.Domain;

namespace Shelf.Api.Controllers;

[ApiController]
[Route("api/cart")]
public class CartController(ShelfDbContext db, ICatalogue catalogue, TimeProvider time) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CartView>> Get(CancellationToken ct)
    {
        if (!CartHeader.TryRead(Request, out var cartId))
            return MissingCartId();

        return await BuildViewAsync(cartId, ct);
    }

    /// <summary>Adds a book to the cart, creating the cart on first use. Adding the same book again increases its quantity.</summary>
    [HttpPost("items")]
    public async Task<ActionResult<CartView>> AddItem(AddCartItemRequest request, CancellationToken ct)
    {
        if (!CartHeader.TryRead(Request, out var cartId))
            return MissingCartId();

        if (request.Quantity < CheckoutValidator.MinQuantity || request.Quantity > CheckoutValidator.MaxQuantity)
            return Problem(statusCode: 400, title: "Bad quantity", detail: CheckoutValidator.BadQuantity(request.BookId));

        var book = await catalogue.GetAsync(request.BookId, ct);
        if (book is null)
            return Problem(statusCode: 400, title: "Unknown book", detail: CheckoutValidator.UnknownBook(request.BookId));

        var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == cartId, ct);
        if (cart is null)
        {
            cart = new Cart { Id = cartId, CreatedAtUtc = time.GetUtcNow().UtcDateTime };
            db.Carts.Add(cart);
        }

        // Store the catalogue's own slug, not whatever casing the client sent.
        var item = cart.Items.SingleOrDefault(i => i.BookId == book.Slug);
        var newQuantity = (item?.Quantity ?? 0) + request.Quantity;
        if (newQuantity > CheckoutValidator.MaxQuantity)
            return Problem(statusCode: 400, title: "Bad quantity", detail: CheckoutValidator.BadQuantity(book.Slug));

        if (item is null)
            cart.Items.Add(new CartItem { BookId = book.Slug, Quantity = request.Quantity });
        else
            item.Quantity = newQuantity;

        await db.SaveChangesAsync(ct);
        return await BuildViewAsync(cartId, ct);
    }

    [HttpDelete("items/{bookId}")]
    public async Task<ActionResult<CartView>> RemoveItem(string bookId, CancellationToken ct)
    {
        if (!CartHeader.TryRead(Request, out var cartId))
            return MissingCartId();

        var item = await db.CartItems.SingleOrDefaultAsync(i => i.CartId == cartId && i.BookId == bookId, ct);
        if (item is not null)
        {
            db.CartItems.Remove(item);
            await db.SaveChangesAsync(ct);
        }

        return await BuildViewAsync(cartId, ct);
    }

    /// <summary>Prices every line from the catalogue at read time. The cart itself stores only book ids and quantities.</summary>
    private async Task<CartView> BuildViewAsync(Guid cartId, CancellationToken ct)
    {
        var items = await db.CartItems.AsNoTracking()
            .Where(i => i.CartId == cartId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct);

        var books = await catalogue.GetManyAsync(items.Select(i => i.BookId), ct);

        var lines = items.Select(i => books.TryGetValue(i.BookId, out var b)
                ? new CartLineView(i.BookId, b.Title, b.Price, i.Quantity, b.Price * i.Quantity, true)
                : new CartLineView(i.BookId, i.BookId, 0m, i.Quantity, 0m, false))
            .ToList();

        var total = CartPricing.Total(lines.Where(l => l.Available)
            .Select(l => new PricedLine(l.BookId, l.Title, l.UnitPrice, l.Quantity)));

        return new CartView(cartId, lines, total);
    }

    private ObjectResult MissingCartId() =>
        Problem(statusCode: 400, title: "Missing cart id", detail: $"Send a GUID in the {CartHeader.Name} header.");
}
