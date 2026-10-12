using System.Security.Cryptography;
using System.Text.Json;
using DocumentDBVectorSamples.Models;
using MongoDB.Bson;

namespace DocumentDBVectorSamples.Services;

public sealed record Fixture(IReadOnlyList<BsonDocument> Documents, double[] Query)
{
    public const int Dimensions = 1536;
    public const string VectorField = "DescriptionVector";
    public const string SharedHash = "60c08c2cdd439444ba8601d94a1f3487f4a9ee56cd48ba201d983c96722de6e4";
    public const string OriginalDotnetHash = "68ca2a0f2a57e095f5c41853301b834779fce30ee7a9eb0aa9b156092945443f";
    public string ExpectedId => Documents[0]["_id"].AsString;

    public static async Task<Fixture> LoadAsync(string dataPath, string queryPath, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(dataPath, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (hash != SharedHash && hash != OriginalDotnetHash)
            throw new ConfigurationException("Fixture checksum mismatch. Use one of the pinned upstream datasets.");
        using var data = JsonDocument.Parse(bytes);
        var docs = ParseDocuments(data.RootElement);
        var query = JsonSerializer.Deserialize<double[]>(await File.ReadAllTextAsync(queryPath, ct))
            ?? throw new ConfigurationException("Query vector is missing.");
        ValidateVector(query);
        var first = docs[0][VectorField].AsBsonArray.Select(v => v.ToDouble()).ToArray();
        if (!query.Zip(first).All(pair => Math.Abs(pair.First - pair.Second) <= 1e-7))
            throw new ConfigurationException("Precomputed smoke query must be the first fixture vector; this is not a semantic quality test.");
        return new Fixture(docs, query);
    }

    public static IReadOnlyList<BsonDocument> ParseDocuments(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() != 50)
            throw new ConfigurationException("Expected exactly 50 pinned fixture documents.");
        var docs = new List<BsonDocument>();
        var ids = new HashSet<string>();
        foreach (var item in data.EnumerateArray())
        {
            var doc = BsonDocument.Parse(item.GetRawText());
            if (!doc.TryGetValue("HotelId", out var hotelId) || !hotelId.IsString ||
                string.IsNullOrWhiteSpace(hotelId.AsString) || !ids.Add(hotelId.AsString))
                throw new ConfigurationException("Fixture HotelId values must be unique strings.");
            var vector = doc[VectorField].AsBsonArray.Select(v => v.ToDouble()).ToArray();
            ValidateVector(vector);
            doc["_id"] = "dotnet-hotel-" + hotelId.AsString;
            doc[VectorField] = new BsonArray(vector.Select(v => new BsonDouble(v)));
            docs.Add(doc);
        }
        return docs;
    }

    public static void ValidateVector(IReadOnlyList<double> vector)
    {
        if (vector.Count != Dimensions || vector.Any(v => !double.IsFinite(v)) || vector.All(v => v == 0))
            throw new ConfigurationException("Expected a finite nonzero 1536-dimensional vector.");
    }
}
