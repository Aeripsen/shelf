using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;

namespace Shelf.Api.Catalogue;

public static class RavenStore
{
    public static IDocumentStore Create(IConfiguration config)
    {
        var store = new DocumentStore
        {
            Urls = new[] { config["RavenDb:Url"] ?? "http://localhost:8081" },
            Database = config["RavenDb:Database"] ?? "Shelf",
        };

        // Single node in Docker. Without this the client asks the server for its cluster topology and may then try
        // the container's internal URL, which is not reachable from outside the compose network.
        store.Conventions.DisableTopologyUpdates = true;

        // Store BookDocument in a collection called "Books" rather than the default "BookDocuments".
        store.Conventions.FindCollectionName = type =>
            type == typeof(BookDocument) ? "Books" : DocumentConventions.DefaultGetCollectionName(type);

        return store.Initialize();
    }
}
