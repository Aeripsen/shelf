namespace Shelf.Api.Catalogue;

/// <summary>The shape stored in RavenDB, one document per book, with ids like "books/pride-and-prejudice".</summary>
public class BookDocument
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Year { get; set; }
    public decimal Price { get; set; }
}
