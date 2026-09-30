using Testcontainers.MsSql;

namespace Shelf.IntegrationTests;

/// <summary>
/// Starts one throwaway SQL Server 2022 container for the whole test run (Testcontainers, on the machine's Docker).
/// If SHELF_TEST_SQL is set, that connection string is used instead and no container is started.
/// </summary>
[SetUpFixture]
public class SqlServerFixture
{
    private static MsSqlContainer? _container;

    public static string ConnectionString { get; private set; } = "";

    [OneTimeSetUp]
    public async Task StartSqlServer()
    {
        var external = Environment.GetEnvironmentVariable("SHELF_TEST_SQL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            return;
        }

        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await _container.StartAsync();

        // Use a named database rather than master so the migrations create everything from scratch.
        ConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "ShelfTests",
        }.ConnectionString;
    }

    [OneTimeTearDown]
    public async Task StopSqlServer()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
