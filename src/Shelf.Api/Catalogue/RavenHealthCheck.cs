using Microsoft.Extensions.Diagnostics.HealthChecks;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;

namespace Shelf.Api.Catalogue;

/// <summary>Healthy when the catalogue database answers a statistics request.</summary>
public class RavenHealthCheck(IDocumentStore store) : IHealthCheck
{
    public const string Name = "ravendb";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var stats = await store.Maintenance.SendAsync(new GetStatisticsOperation(), ct);
            return HealthCheckResult.Healthy($"{stats.CountOfDocuments} documents in {store.Database}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"RavenDB database {store.Database} is not reachable", ex);
        }
    }
}
