namespace Shelf.Core.Catalogue;

/// <summary>Read access to the product catalogue. The API implements it over RavenDB; tests use a fake.</summary>
public interface ICatalogue
{
    Task<IReadOnlyList<Book>> GetAllAsync(CancellationToken ct = default);

    Task<Book?> GetAsync(string slug, CancellationToken ct = default);

    /// <summary>Returns the books that exist, keyed by slug. Missing slugs are simply absent from the result.</summary>
    Task<IReadOnlyDictionary<string, Book>> GetManyAsync(IEnumerable<string> slugs, CancellationToken ct = default);
}
