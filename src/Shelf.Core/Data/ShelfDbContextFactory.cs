using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shelf.Core.Data;

/// <summary>
/// Used only by the `dotnet ef` tools to build the model when adding a migration. It never opens a connection,
/// so the connection string here is a placeholder. The running services read theirs from configuration.
/// </summary>
public class ShelfDbContextFactory : IDesignTimeDbContextFactory<ShelfDbContext>
{
    public ShelfDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShelfDbContext>()
            .UseSqlServer("Server=localhost,1433;Database=Shelf;TrustServerCertificate=True")
            .Options;
        return new ShelfDbContext(options);
    }
}
