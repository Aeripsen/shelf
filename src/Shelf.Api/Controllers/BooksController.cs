using Microsoft.AspNetCore.Mvc;
using Shelf.Core.Catalogue;

namespace Shelf.Api.Controllers;

[ApiController]
[Route("api/books")]
public class BooksController(ICatalogue catalogue) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<Book>> GetAll(CancellationToken ct) => await catalogue.GetAllAsync(ct);

    [HttpGet("{slug}")]
    public async Task<ActionResult<Book>> Get(string slug, CancellationToken ct)
    {
        var book = await catalogue.GetAsync(slug, ct);
        return book is null ? NotFound() : book;
    }
}
