using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Shelf.IntegrationTests;

/// <summary>
/// Starts one throwaway SQL Server 2022 container for the whole test run (Testcontainers, on the machine's Docker).
/// If SHELF_TEST_SQL is set, that server is used instead and no container is started.
/// Each fixture works in its own database (see <see cref="ConnectionStringFor"/>), so no test depends on another
/// fixture having created or migrated anything first.
/// </summary>
[SetUpFixture]
public class SqlServerFixture
{
    private static MsSqlContainer? _container;
    private static string _server = "";

    [OneTimeSetUp]
    public async Task StartSqlServer()
    {
        var external = Environment.GetEnvironmentVariable("SHELF_TEST_SQL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _server = external;
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();
        _server = _container.GetConnectionString();
    }

    /// <summary>The server's connection string pointed at <paramref name="database"/>, which need not exist yet.</summary>
    public static string ConnectionStringFor(string database) =>
        new SqlConnectionStringBuilder(_server) { InitialCatalog = database }.ConnectionString;

    [OneTimeTearDown]
    public async Task StopSqlServer()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
