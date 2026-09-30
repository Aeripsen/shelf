using Microsoft.EntityFrameworkCore;
using Shelf.Core.Data;

namespace Shelf.Api.Startup;

/// <summary>
/// Applies the committed EF Core migrations when the API starts, before it accepts requests. The fulfilment worker
/// never migrates. In a real deployment this would be a separate step (a migration bundle or a script) so several
/// instances do not race and the app does not need schema rights.
/// </summary>
public class DatabaseMigrator(IServiceProvider services, IConfiguration config, ILogger<DatabaseMigrator> log) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!config.GetValue("Startup:MigrateDatabase", true))
            return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShelfDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        await db.Database.MigrateAsync(ct);
        log.LogInformation("Applied {Count} EF Core migration(s): {Names}", pending.Count, string.Join(", ", pending));
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
