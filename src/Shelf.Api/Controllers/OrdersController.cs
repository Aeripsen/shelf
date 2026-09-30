using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shelf.Core.Data;

namespace Shelf.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController(ShelfDbContext db) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderView>> Get(Guid id, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == id, ct);

        if (order is null)
            return NotFound();

        return new OrderView(
            order.Id,
            order.Email,
            order.Status,
            order.Total,
            order.PlacedAtUtc,
            order.FulfilledAtUtc,
            order.Lines.OrderBy(l => l.Id).Select(l => new OrderLineView(l.BookId, l.Title, l.UnitPrice, l.Quantity)).ToList());
    }
}
