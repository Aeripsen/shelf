using System.Text.RegularExpressions;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Queries;
using Shelf.Core.Catalogue;

namespace Shelf.Api.Catalogue;

/// <param name="pageSize">Documents per round trip when listing the whole catalogue. Tests make it small so the
/// multi-page path runs against 10 books.</param>
public partial class RavenCatalogue(IDocumentStore store, int pageSize = RavenCatalogue.DefaultPageSize) : ICatalogue
{
    public const string IdPrefix = "books/";
    public const int DefaultPageSize = 256;
    public const int MaxSearchResults = 50;

    public async Task<IReadOnlyList<Book>> GetAllAsync(CancellationToken ct = default)
    {
        // Loading by id prefix reads the documents directly instead of querying an index, so the list is never
        // stale, even right after the seed runs. The API returns the whole catalogue in one response; internally it
        // is read pageSize documents at a time until a short page says there is no more.
        var books = new List<Book>();
        for (var start = 0; ; start += pageSize)
        {
            using var session = store.OpenAsyncSession();
            var page = (await session.Advanced.LoadStartingWithAsync<BookDocument>(IdPrefix, start: start, pageSize: pageSize, token: ct)).ToList();
            books.AddRange(page.Select(ToBook));
            if (page.Count < pageSize)
                break;
        }

        return books.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<Book>> SearchAsync(string text, CancellationToken ct = default)
    {
        // Keep letters and digits only, so user input can never inject query syntax, and make each word a prefix
        // match: "aust" finds Austen. Every word must match (AND), in the title or the author.
        var words = NonWord().Split(text).Where(w => w.Length > 0).Select(w => w.ToLowerInvariant() + "*").ToList();
        if (words.Count == 0)
            return Array.Empty<Book>();

        using var session = store.OpenAsyncSession();
        var docs = await session.Query<Books_Search.Entry, Books_Search>()
            // An index catches up asynchronously after writes. Catalogue writes are rare (the seed), so wait briefly
            // for it rather than risk a search that misses a book that was just stored.
            .Customize(q => q.WaitForNonStaleResults(TimeSpan.FromSeconds(5)))
            .Search(e => e.Query, string.Join(' ', words), @operator: SearchOperator.And)
            .OfType<BookDocument>()
            .Take(MaxSearchResults)
            .ToListAsync(ct);

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

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();
}
