using Microsoft.AspNetCore.Mvc;
using Shelf.Core.Catalogue;

namespace Shelf.Api.Controllers;

[ApiController]
[Route("api/books")]
public class BooksController(ICatalogue catalogue) : ControllerBase
{
    /// <summary>The whole catalogue sorted by title, or with <c>q</c> a full-text search over title and author.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<Book>> GetAll([FromQuery] string? q, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(q) ? await catalogue.GetAllAsync(ct) : await catalogue.SearchAsync(q, ct);

    [HttpGet("{slug}")]
    public async Task<ActionResult<Book>> Get(string slug, CancellationToken ct)
    {
        var book = await catalogue.GetAsync(slug, ct);
        return book is null ? NotFound() : book;
    }
}
