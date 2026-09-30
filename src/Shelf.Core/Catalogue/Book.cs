namespace Shelf.Core.Catalogue;

/// <summary>A book as the rest of the system sees it. Slug is the part of the RavenDB id after "books/".</summary>
public record Book(string Slug, string Title, string Author, int Year, decimal Price);
