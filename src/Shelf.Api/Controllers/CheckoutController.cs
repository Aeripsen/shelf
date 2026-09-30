using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shelf.Contracts;
using Shelf.Core.Catalogue;
using Shelf.Core.Data;
using Shelf.Core.Domain;

namespace Shelf.Api.Controllers;

[ApiController]
[Route("api/checkout")]
public class CheckoutController(
    ShelfDbContext db,
    ICatalogue catalogue,
    IPublishEndpoint publish,
    TimeProvider time,
    ILogger<CheckoutController> log) : ControllerBase
{
    /// <summary>
    /// Turns the cart into a Placed order and returns at once. Fulfilment happens later in the worker, so a slow or
    /// stopped fulfilment service never holds up the purchase.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        if (!CartHeader.TryRead(Request, out var cartId))
            return Problem(statusCode: 400, title: "Missing cart id", detail: $"Send a GUID in the {CartHeader.Name} header.");

        var cart = await db.Carts.Include(c => c.Items).SingleOrDefaultAsync(c => c.Id == cartId, ct);
        var items = cart?.Items ?? new List<CartItem>();

        // Prices come from the catalogue, never from the client.
        var books = await catalogue.GetManyAsync(items.Select(i => i.BookId), ct);

        var errors = CheckoutValidator.Validate(request.Email, items, books);
        if (errors.Count > 0)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Checkout rejected",
                Detail = string.Join(" ", errors),
            };
            problem.Extensions["errors"] = errors;
            return BadRequest(problem);
        }

        var order = OrderFactory.Create(Guid.NewGuid(), request.Email!, items, books, time.GetUtcNow().UtcDateTime);
        db.Orders.Add(order);
        db.CartItems.RemoveRange(items);

        // One SaveChanges is one transaction: the new order and the emptied cart commit together or not at all.
        // Each cart item is deleted with its rowversion in the WHERE clause, so if the cart changed after we read it
        // (a quantity bumped in another tab, or a second checkout of the same cart) nothing is written and the
        // shopper is asked to review the cart, instead of an order that no longer matches it.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cart changed",
                detail: "Your cart changed while you were checking out. Review it and place the order again.");
        }

        // Published after the commit, so the worker never sees an order that is not in the database yet.
        // Known gap: if the broker is unreachable right here, the order is saved but never published (a dual write).
        // The fix is a transactional outbox; see Limitations in the README.
        await publish.Publish(new OrderPlaced(order.Id, order.Total, order.PlacedAtUtc), ct);
        log.LogInformation("Order {OrderId} placed, total {Total}, OrderPlaced published", order.Id, order.Total);

        return Created($"/api/orders/{order.Id}", new CheckoutResponse(order.Id, order.Status, order.Total));
    }
}
