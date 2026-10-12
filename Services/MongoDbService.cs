using DocumentDBVectorSamples.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace DocumentDBVectorSamples.Services;

public interface IVectorStore
{
    Task<bool> ContainsIdAsync(string id, CancellationToken ct);
    Task InsertAsync(BsonDocument document, CancellationToken ct);
    Task<IReadOnlyList<BsonDocument>> ListIndexesAsync(CancellationToken ct);
    Task CreateIndexAsync(BsonDocument index, CancellationToken ct);
    Task<IReadOnlyList<BsonDocument>> SearchAsync(BsonDocument[] pipeline, CancellationToken ct);
}

/// <summary>Only the explicitly configured namespace; no database enumeration, delete, update or drop APIs.</summary>
public sealed class MongoDbService(IMongoDatabase database, string collectionName) : IVectorStore
{
    private readonly IMongoCollection<BsonDocument> collection = database.GetCollection<BsonDocument>(collectionName);

    public async Task<bool> ContainsIdAsync(string id, CancellationToken ct) =>
        await collection.Find(new BsonDocument("_id", id)).Limit(1).AnyAsync(ct);

    public async Task InsertAsync(BsonDocument document, CancellationToken ct)
    {
        try { await collection.InsertOneAsync(document, cancellationToken: ct); }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { throw new DuplicateDocumentException(); }
    }

    public async Task<IReadOnlyList<BsonDocument>> ListIndexesAsync(CancellationToken ct)
    {
        try
        {
            using var cursor = await collection.Indexes.ListAsync(cancellationToken: ct);
            return await cursor.ToListAsync(ct);
        }
        catch (MongoCommandException e) when (e.Code == 26) { return []; }
    }

    public async Task CreateIndexAsync(BsonDocument index, CancellationToken ct) =>
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
            { ["createIndexes"] = collectionName, ["indexes"] = new BsonArray { index } }, cancellationToken: ct);

    public async Task<IReadOnlyList<BsonDocument>> SearchAsync(BsonDocument[] pipeline, CancellationToken ct)
    {
        using var cursor = await collection.AggregateAsync<BsonDocument>(pipeline,
            new AggregateOptions { MaxTime = TimeSpan.FromSeconds(30) }, ct);
        return await cursor.ToListAsync(ct);
    }
}
