using Raven.Client.Documents;
using Shelf.Core.Catalogue;

namespace Shelf.Api.Catalogue;

public class RavenCatalogue(IDocumentStore store) : ICatalogue
{
    public const string IdPrefix = "books/";

    public async Task<IReadOnlyList<Book>> GetAllAsync(CancellationToken ct = default)
    {
        using var session = store.OpenAsyncSession();

        // Loading by id prefix reads the documents directly instead of querying an index, so the list is never
        // stale, even right after the seed runs.
        var docs = await session.Advanced.LoadStartingWithAsync<BookDocument>(IdPrefix, pageSize: 1024, token: ct);

        return docs.Select(ToBook).OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Book?> GetAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        using var session = store.OpenAsyncSession();
        var doc = await session.LoadAsync<BookDocument>(IdPrefix + slug, ct);
        return doc is null ? null : ToBook(doc);
    }

    public async Task<IReadOnlyDictionary<string, Book>> GetManyAsync(IEnumerable<string> slugs, CancellationToken ct = default)
    {
        var ids = slugs.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().Select(s => IdPrefix + s).ToList();
        if (ids.Count == 0)
            return new Dictionary<string, Book>();

        using var session = store.OpenAsyncSession();

        // One round trip for all the ids. Missing documents come back as null values.
        var docs = await session.LoadAsync<BookDocument>(ids, ct);

        return docs.Values
            .Where(d => d is not null)
            .Select(ToBook)
            .ToDictionary(b => b.Slug, StringComparer.OrdinalIgnoreCase);
    }

    private static Book ToBook(BookDocument d) =>
        new(d.Id[IdPrefix.Length..], d.Title, d.Author, d.Year, d.Price);
}
