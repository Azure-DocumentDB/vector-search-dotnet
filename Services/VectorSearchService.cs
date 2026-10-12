using DocumentDBVectorSamples.Models;
using MongoDB.Bson;

namespace DocumentDBVectorSamples.Services;

public sealed class VectorSearchService(IVectorStore store)
{
    public async Task<SearchReport> RunAsync(VectorIndexType type, Fixture fixture, bool allowWrites, CancellationToken ct)
    {
        if (!allowWrites) throw new ConfigurationException("Database writes require explicit opt-in.");
        var index = VectorSchema.Index(type);
        VectorSchema.CheckCatalog(await store.ListIndexesAsync(ct), index);
        var inserted = 0;
        foreach (var document in fixture.Documents)
        {
            ct.ThrowIfCancellationRequested();
            var id = document["_id"].AsString;
            if (await store.ContainsIdAsync(id, ct)) continue;
            try { await store.InsertAsync(document, ct); inserted++; }
            catch (DuplicateDocumentException)
            {
                // Only tolerate an exact _id race; other unique-index conflicts are real failures.
                if (!await store.ContainsIdAsync(id, ct)) throw;
            }
        }
        // Always ensure/verify the index, even for a populated collection.
        if (!VectorSchema.CheckCatalog(await store.ListIndexesAsync(ct), index))
            await store.CreateIndexAsync(index, ct);
        if (!VectorSchema.CheckCatalog(await store.ListIndexesAsync(ct), index)) throw new VerificationException();

        // Bounded readiness retries; cancellation and real command failures always propagate.
        IReadOnlyList<BsonDocument> results = [];
        for (var attempt = 0; attempt < 12; attempt++)
        {
            results = await store.SearchAsync(VectorSchema.Pipeline(fixture.Query), ct);
            if (results.Count == 5 && results[0].GetValue("_id", "") == fixture.ExpectedId) break;
            if (attempt < 11) await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        if (results.Count != 5 || results[0].GetValue("_id", "") != fixture.ExpectedId ||
            results.Select(r => r.GetValue("_id", "")).Distinct().Count() != 5 ||
            results.Any(r => !r.Contains("score") || !double.IsFinite(r["score"].ToDouble())) ||
            Math.Abs(results[0]["score"].ToDouble() - 1.0) > 0.001)
            throw new VerificationException();
        return new SearchReport(type.Suffix(), inserted, fixture.Documents.Count - inserted, results.Count,
            results[0]["_id"].AsString, true, "precomputed fixture self-match; no model call or semantic-quality claim");
    }
}

public sealed record SearchReport(string Algorithm, int Inserted, int Preserved, int Results,
    string FirstId, bool IndexVerified, string Validation);

public static class VectorSchema
{
    public static BsonDocument Index(VectorIndexType type)
    {
        var options = new BsonDocument { ["kind"] = "vector-" + type.Suffix(), ["similarity"] = "COS", ["dimensions"] = Fixture.Dimensions };
        switch (type)
        {
            case VectorIndexType.IVF: options["numLists"] = 1; break;
            case VectorIndexType.HNSW: options["m"] = 16; options["efConstruction"] = 64; break;
            case VectorIndexType.DiskANN: break;
            default: throw new ConfigurationException("Unknown algorithm.");
        }
        return new BsonDocument { ["name"] = "dotnet_vector_" + type.Suffix(),
            ["key"] = new BsonDocument(Fixture.VectorField, "cosmosSearch"), ["cosmosSearchOptions"] = options };
    }

    public static bool CheckCatalog(IReadOnlyList<BsonDocument> catalog, BsonDocument expected)
    {
        var found = false;
        foreach (var actual in catalog)
        {
            var sameName = actual.GetValue("name", "") == expected["name"];
            var vector = actual.TryGetValue("key", out var key) && key.IsBsonDocument &&
                key.AsBsonDocument.Elements.Any(e => e.Value == "cosmosSearch");
            if (!sameName && !vector) continue;
            if (!sameName || actual.GetValue("unique", false) == true || actual.GetValue("sparse", false) == true ||
                actual.Contains("partialFilterExpression") || actual.Contains("expireAfterSeconds") ||
                !actual.TryGetValue("key", out var actualKey) || actualKey != expected["key"] ||
                !actual.TryGetValue("cosmosSearchOptions", out var options) || !options.IsBsonDocument ||
                !expected["cosmosSearchOptions"].AsBsonDocument.All(e =>
                    options.AsBsonDocument.TryGetValue(e.Name, out var value) && value == e.Value))
                throw new IndexConflictException();
            found = true;
        }
        return found;
    }

    public static BsonDocument[] Pipeline(double[] vector)
    {
        Fixture.ValidateVector(vector);
        return [new BsonDocument("$search", new BsonDocument("cosmosSearch", new BsonDocument
                { ["vector"] = new BsonArray(vector), ["path"] = Fixture.VectorField, ["k"] = 5 })),
            new BsonDocument("$project", new BsonDocument { ["_id"] = 1, ["score"] = new BsonDocument("$meta", "searchScore") })];
    }
}
