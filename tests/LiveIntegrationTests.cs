using DocumentDBVectorSamples.Models;
using DocumentDBVectorSamples.Services;
using DocumentDBVectorSamples.Utilities;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit.Sdk;

namespace DocumentDBVectorSamples.Tests;

/// <summary>Explicit paid-resource gate. Selecting Category=Live without its guards FAILS, never skips.</summary>
public class LiveIntegrationTests
{
    public static AppConfiguration Configuration(string mode, Func<string, string?> get)
    {
        if (get("RUN_LIVE_INTEGRATION") != "true" || get("ALLOW_DATABASE_WRITES") != "true" ||
            get("LIVE_AUTH_MODES") != "scram,oidc")
            throw new ConfigurationException("Live gate requires RUN_LIVE_INTEGRATION=true, ALLOW_DATABASE_WRITES=true and LIVE_AUTH_MODES=scram,oidc.");
        foreach (var key in new[] { "MONGO_URI", "MONGO_ALLOWED_HOST", "MONGO_DATABASE", "MONGO_COLLECTION_PREFIX", "MONGO_USERNAME", "MONGO_PASSWORD", "VECTOR_DATA_FILE", "QUERY_VECTOR_FILE" })
            if (string.IsNullOrWhiteSpace(get(key))) throw new ConfigurationException("Missing required live environment. See README.");
        return AppConfiguration.Read(key => key switch
        {
            "MONGO_AUTH_MODE" => mode,
            "MONGO_PASSWORD" when mode == "oidc" => "",
            "MONGO_COLLECTION_PREFIX" => get(key) + "_" + mode,
            _ => get(key)
        });
    }

    [Theory, Trait("Category", "Live")]
    [InlineData("scram", VectorIndexType.IVF)][InlineData("scram", VectorIndexType.HNSW)][InlineData("scram", VectorIndexType.DiskANN)]
    [InlineData("oidc", VectorIndexType.IVF)][InlineData("oidc", VectorIndexType.HNSW)][InlineData("oidc", VectorIndexType.DiskANN)]
    public async Task CatalogTop5SelfMatchAndIdempotence(string mode, VectorIndexType algorithm)
    {
        var stage = "configuration";
        try
        {
            var config = Configuration(mode, Environment.GetEnvironmentVariable);
            config.RequireWrites();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
            var ct = timeout.Token;
            stage = "fixture-validation";
            var fixture = await Fixture.LoadAsync(config.DataFile, config.QueryFile, ct);
            stage = "client-construction";
            using var client = new MongoClient(ClientFactory.Settings(config));
            var database = client.GetDatabase(config.Database);
            var store = new MongoDbService(database, config.Collection(algorithm));
            var collection = database.GetCollection<BsonDocument>(config.Collection(algorithm));
            // Seed only absent test IDs. Never overwrite a document, even a prior test's canary.
            var edited = fixture.Documents[0].DeepClone().AsBsonDocument;
            edited["HotelName"] = "live gate preservation canary";
            stage = "canary-insertion";
            await InsertIfAbsent(store, edited, ct);
            await InsertIfAbsent(store, new BsonDocument { ["_id"] = "dotnet-unrelated-canary", ["note"] = "preserve" }, ct);
            var beforeEdited = await collection.Find(new BsonDocument("_id", fixture.ExpectedId)).SingleAsync(ct);
            var beforeUnrelated = await collection.Find(new BsonDocument("_id", "dotnet-unrelated-canary")).SingleAsync(ct);
            var service = new VectorSearchService(store);
            stage = "first-index-and-query-pass";
            var first = await service.RunAsync(algorithm, fixture, true, ct);
            var count = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct);
            stage = "repeat-index-and-query-pass";
            var second = await service.RunAsync(algorithm, fixture, true, ct);
            stage = "preservation-verification";
            Assert.Equal(5, first.Results); Assert.Equal(fixture.ExpectedId, first.FirstId);
            Assert.Equal(0, second.Inserted); Assert.True(second.IndexVerified);
            Assert.Equal(count, await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: ct));
            Assert.True(count >= 51);
            Assert.Equal(beforeEdited, await collection.Find(new BsonDocument("_id", fixture.ExpectedId)).SingleAsync(ct));
            Assert.Equal(beforeUnrelated, await collection.Find(new BsonDocument("_id", "dotnet-unrelated-canary")).SingleAsync(ct));
            Assert.True(VectorSchema.CheckCatalog(await store.ListIndexesAsync(ct), VectorSchema.Index(algorithm)));
        }
        catch (Exception)
        {
            // xUnit normally prints raw exception messages, which may contain service metadata/credentials.
            throw new XunitException($"Live {mode}/{algorithm.Suffix()} gate failed at {stage}. Check guards, namespace, credentials, network, service support and fixture. No raw exception logged.");
        }
    }

    private static async Task InsertIfAbsent(IVectorStore store, BsonDocument document, CancellationToken ct)
    {
        var id = document["_id"].AsString;
        if (await store.ContainsIdAsync(id, ct)) return;
        try { await store.InsertAsync(document, ct); }
        catch (DuplicateDocumentException) { if (!await store.ContainsIdAsync(id, ct)) throw; }
    }
}

public class LiveGuardTests
{
    [Fact] public void MissingGuardFailsInsteadOfSkipping() => Assert.Throws<ConfigurationException>(() => LiveIntegrationTests.Configuration("scram", _ => null));
    [Fact] public void RequestedWithoutEnvironmentFails() => Assert.Throws<ConfigurationException>(() => LiveIntegrationTests.Configuration("scram", key => key switch
    { "RUN_LIVE_INTEGRATION" or "ALLOW_DATABASE_WRITES" => "true", "LIVE_AUTH_MODES" => "scram,oidc", _ => null }));
    [Fact] public void PartialAuthSelectionDoesNotClaimCompleteGate() => Assert.Throws<ConfigurationException>(() => LiveIntegrationTests.Configuration("scram", key => key switch
    { "RUN_LIVE_INTEGRATION" or "ALLOW_DATABASE_WRITES" => "true", "LIVE_AUTH_MODES" => "scram", _ => "placeholder" }));
}
