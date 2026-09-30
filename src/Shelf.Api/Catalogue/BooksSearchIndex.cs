using Raven.Client.Documents.Indexes;

namespace Shelf.Api.Catalogue;

/// <summary>
/// RavenDB static index for the storefront search box. It puts each book's title and author into one full-text
/// field, so "austen pride" matches a book whose author has one word and whose title has the other.
/// Deployed by CatalogueSeeder at startup. Named Books/Search on the server.
/// </summary>
public class Books_Search : AbstractIndexCreationTask<BookDocument, Books_Search.Entry>
{
    public class Entry
    {
        public string[] Query { get; set; } = Array.Empty<string>();
    }

    public Books_Search()
    {
        Map = books => from book in books
                       select new Entry { Query = new[] { book.Title, book.Author } };

        Index(e => e.Query, FieldIndexing.Search);
    }
}
