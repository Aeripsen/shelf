using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shelf.Core.Data;

namespace Shelf.Api.Startup;

/// <summary>Healthy when the orders database accepts a connection.</summary>
public class SqlServerHealthCheck(ShelfDbContext db) : IHealthCheck
{
    public const string Name = "sqlserver";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("SQL Server is not reachable");
}
