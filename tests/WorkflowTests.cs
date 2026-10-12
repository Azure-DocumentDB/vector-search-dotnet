using DocumentDBVectorSamples.Models;
using DocumentDBVectorSamples.Services;
using MongoDB.Bson;

namespace DocumentDBVectorSamples.Tests;

public class WorkflowTests
{
    internal static Fixture SyntheticFixture()
    {
        var query = new double[1536]; query[0] = 1;
        var docs = Enumerable.Range(1, 50).Select(i => new BsonDocument { ["_id"] = "dotnet-hotel-" + i,
            ["HotelId"] = i.ToString(), ["HotelName"] = "synthetic " + i, [Fixture.VectorField] = new BsonArray(query) }).ToList();
        return new Fixture(docs, query);
    }

    [Theory][InlineData(VectorIndexType.IVF)][InlineData(VectorIndexType.HNSW)][InlineData(VectorIndexType.DiskANN)]
    public async Task EnsureIndexAndRerunPreserveUserEditsAndUnrelatedRecords(VectorIndexType type)
    {
        var fixture = SyntheticFixture();
        var fake = new FakeStore();
        var userEdit = fixture.Documents[0].DeepClone().AsBsonDocument;
        userEdit["HotelName"] = "user edited, do not overwrite";
        fake.Documents.Add(fixture.ExpectedId, userEdit);
        fake.Documents.Add("unrelated", new BsonDocument("_id", "unrelated"));
        var service = new VectorSearchService(fake);
        var first = await service.RunAsync(type, fixture, true, default);
        var second = await service.RunAsync(type, fixture, true, default);
        Assert.Equal(49, first.Inserted); Assert.Equal(0, second.Inserted);
        Assert.Equal(51, fake.Documents.Count);
        Assert.Equal("user edited, do not overwrite", fake.Documents[fixture.ExpectedId]["HotelName"]);
        Assert.True(fake.Documents.ContainsKey("unrelated"));
        Assert.Single(fake.Indexes); Assert.Equal(1, fake.CreateCalls);
        Assert.True(VectorSchema.CheckCatalog(fake.Indexes, VectorSchema.Index(type)));
        Assert.Equal(5, second.Results); Assert.Equal(fixture.ExpectedId, second.FirstId);
        Assert.Equal(5, fake.LastPipeline![0]["$search"]["cosmosSearch"]["k"].AsInt32);
        Assert.Equal(1536, fake.LastPipeline[0]["$search"]["cosmosSearch"]["vector"].AsBsonArray.Count);
    }

    [Fact] public async Task PopulatedCollectionStillGetsMissingIndex()
    {
        var fake = new FakeStore(); var fixture = SyntheticFixture();
        foreach (var doc in fixture.Documents) fake.Documents.Add(doc["_id"].AsString, doc);
        var result = await new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, fixture, true, default);
        Assert.Equal(0, result.Inserted); Assert.Single(fake.Indexes);
    }

    [Fact] public async Task InsertionFailureIsNeverSwallowed()
    {
        var fake = new FakeStore { InsertFailure = new InvalidOperationException("sentinel secret") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, SyntheticFixture(), true, default));
        Assert.Equal(0, fake.CreateCalls); Assert.Null(fake.LastPipeline);
    }

    [Fact] public async Task UnrelatedDuplicateKeyFailureIsNotSuccess()
    {
        var fake = new FakeStore { InsertFailure = new DuplicateDocumentException() };
        await Assert.ThrowsAsync<DuplicateDocumentException>(() => new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, SyntheticFixture(), true, default));
    }

    [Fact] public async Task SameIdInsertRaceIsPreserved()
    {
        var fake = new FakeStore { RaceInsert = true };
        var result = await new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, SyntheticFixture(), true, default);
        Assert.Equal(0, result.Inserted); Assert.Equal(50, fake.Documents.Count);
    }

    [Fact] public async Task NoOptInMeansNoStoreCalls()
    {
        var fake = new FakeStore();
        await Assert.ThrowsAsync<ConfigurationException>(() => new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, SyntheticFixture(), false, default));
        Assert.Equal(0, fake.CatalogCalls); Assert.Empty(fake.Documents);
    }

    [Theory][InlineData("name")][InlineData("dimensions")][InlineData("kind")][InlineData("similarity")][InlineData("field")]
    public async Task ConflictingIndexesFailBeforeInsertAndNeverDrop(string field)
    {
        var bad = VectorSchema.Index(VectorIndexType.IVF);
        if (field == "name") bad["name"] = "user_index";
        else if (field == "field") bad["key"] = new BsonDocument("OtherField", "cosmosSearch");
        else bad["cosmosSearchOptions"][field] = field == "dimensions" ? new BsonInt32(42) : new BsonString("wrong");
        var fake = new FakeStore(); fake.Indexes.Add(bad);
        await Assert.ThrowsAsync<IndexConflictException>(() => new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, SyntheticFixture(), true, default));
        Assert.Empty(fake.Documents); Assert.Same(bad, fake.Indexes[0]); Assert.Equal(0, fake.CreateCalls);
    }

    [Theory][InlineData("unique")][InlineData("sparse")][InlineData("partialFilterExpression")][InlineData("expireAfterSeconds")]
    public void AdditionalIndexSemanticsAreConflicts(string option)
    {
        var index = VectorSchema.Index(VectorIndexType.IVF);
        index[option] = true;
        Assert.Throws<IndexConflictException>(() => VectorSchema.CheckCatalog([index], VectorSchema.Index(VectorIndexType.IVF)));
    }

    [Fact] public async Task CreatedIndexMustActuallyAppearInCatalog()
    {
        var fake = new FakeStore { HideCreatedIndex = true };
        await Assert.ThrowsAsync<VerificationException>(() => new VectorSearchService(fake).RunAsync(VectorIndexType.IVF, SyntheticFixture(), true, default));
    }

    [Fact] public async Task SearchFailuresPropagate()
    {
        var fake = new FakeStore { SearchFailure = new InvalidOperationException("unsupported algorithm") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VectorSearchService(fake).RunAsync(VectorIndexType.DiskANN, SyntheticFixture(), true, default));
    }

    [Fact] public async Task CancellationPropagates()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VectorSearchService(new FakeStore()).RunAsync(VectorIndexType.IVF, SyntheticFixture(), true, cts.Token));
    }

    [Theory][InlineData(0)][InlineData(1535)][InlineData(1537)]
    public void WrongDimensionRejected(int size) => Assert.Throws<ConfigurationException>(() => Fixture.ValidateVector(new double[size]));
    [Fact] public void NanRejected() { var q = SyntheticFixture().Query; q[0] = double.NaN; Assert.Throws<ConfigurationException>(() => Fixture.ValidateVector(q)); }
    [Fact] public void ZeroRejected() => Assert.Throws<ConfigurationException>(() => Fixture.ValidateVector(new double[1536]));

    internal sealed class FakeStore : IVectorStore
    {
        public Dictionary<string, BsonDocument> Documents { get; } = [];
        public List<BsonDocument> Indexes { get; } = [];
        public Exception? InsertFailure { get; init; }
        public Exception? SearchFailure { get; init; }
        public bool RaceInsert { get; init; }
        public bool HideCreatedIndex { get; init; }
        public int CreateCalls { get; private set; }
        public int CatalogCalls { get; private set; }
        public BsonDocument[]? LastPipeline { get; private set; }
        public Task<bool> ContainsIdAsync(string id, CancellationToken ct) => Task.FromResult(Documents.ContainsKey(id));
        public Task InsertAsync(BsonDocument document, CancellationToken ct)
        {
            if (InsertFailure != null) throw InsertFailure;
            Documents.Add(document["_id"].AsString, document.DeepClone().AsBsonDocument);
            if (RaceInsert) throw new DuplicateDocumentException();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<BsonDocument>> ListIndexesAsync(CancellationToken ct)
        { CatalogCalls++; return Task.FromResult<IReadOnlyList<BsonDocument>>(Indexes); }
        public Task CreateIndexAsync(BsonDocument index, CancellationToken ct)
        { CreateCalls++; if (!HideCreatedIndex) Indexes.Add(index); return Task.CompletedTask; }
        public Task<IReadOnlyList<BsonDocument>> SearchAsync(BsonDocument[] pipeline, CancellationToken ct)
        {
            if (SearchFailure != null) throw SearchFailure;
            LastPipeline = pipeline;
            return Task.FromResult<IReadOnlyList<BsonDocument>>(Enumerable.Range(1, 5).Select(i => new BsonDocument { ["_id"] = "dotnet-hotel-" + i, ["score"] = 1.0 }).ToList());
        }
    }
}
