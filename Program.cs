using System.Text.Json;
using DocumentDBVectorSamples.Models;
using DocumentDBVectorSamples.Services;
using DocumentDBVectorSamples.Utilities;
using MongoDB.Driver;

namespace DocumentDBVectorSamples;

public enum VectorIndexType { IVF, HNSW, DiskANN }
public static class AlgorithmNames
{
    public static string Suffix(this VectorIndexType type) => type switch
    {
        VectorIndexType.IVF => "ivf", VectorIndexType.HNSW => "hnsw", VectorIndexType.DiskANN => "diskann",
        _ => throw new ConfigurationException("Unknown algorithm.")
    };
}

public static class Program
{
    public static Task<int> Main(string[] args) => RunAsync(args, Console.In, Console.Out, Console.Error);

    public static async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, TextWriter error,
        Func<string, string?>? environment = null, Func<string, CancellationToken, Task>? execute = null)
    {
        try
        {
            if (args is ["--help"] or ["help"])
            {
                await output.WriteLineAsync("Usage: dotnet run -- [ivf|hnsw|diskann|all|show-indexes|validate-fixture|embed-query]\nNo args opens the menu. See README for mandatory namespace/auth/write guards.");
                return 0;
            }
            string? command;
            if (args.Length == 0)
            {
                while (true)
                {
                    await output.WriteLineAsync("DocumentDB precomputed fixture smoke test (no model calls)\n1. Optional model query\n2. Configured namespace indexes\n3. IVF\n4. HNSW\n5. DiskANN\n0. Exit");
                    var line = await input.ReadLineAsync();
                    if (line is null or "0") return 0;
                    command = line switch { "1" => "embed-query", "2" => "show-indexes", "3" => "ivf", "4" => "hnsw", "5" => "diskann", _ => null };
                    if (command != null) break;
                    await output.WriteLineAsync("Invalid selection.");
                }
            }
            else if (args.Length == 1 && args[0] is "ivf" or "hnsw" or "diskann" or "all" or "show-indexes" or "validate-fixture" or "embed-query")
                command = args[0];
            else throw new ConfigurationException("Unknown command. Use --help.");

            var get = environment ?? Environment.GetEnvironmentVariable;
            if (execute != null) { await execute(command, CancellationToken.None); return 0; }
            if (command == "validate-fixture")
            {
                using var dataTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var data = await Fixture.LoadAsync(get("VECTOR_DATA_FILE") ?? "data/Hotels_Vector.json",
                    get("QUERY_VECTOR_FILE") ?? "data/query-vector.json", dataTimeout.Token);
                await output.WriteLineAsync(JsonSerializer.Serialize(new { Records = data.Documents.Count,
                    Dimensions = data.Query.Length, Validation = "pinned fixture and first-record query verified; no network or model call" }));
                return 0;
            }
            if (command == "embed-query")
            {
                using var modelTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await EmbeddingService.CreateQueryAsync(get("QUERY_TEXT") ?? "", get("AZURE_OPENAI_ENDPOINT") ?? "",
                    get("AZURE_OPENAI_ALLOWED_HOST") ?? "", get("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") ?? "",
                    get("ALLOW_MODEL_CALLS") == "true", modelTimeout.Token);
                await output.WriteLineAsync("Generated a 1536-dimensional query embedding (not persisted or searched). Optional live model behavior is not covered by the fixture gate.");
                return 0;
            }
            var config = AppConfiguration.Read(get);
            if (command != "show-indexes") config.RequireWrites();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds));
            var fixture = command == "show-indexes" ? null : await Fixture.LoadAsync(config.DataFile, config.QueryFile, timeout.Token);
            using var client = new MongoClient(ClientFactory.Settings(config));
            var types = command is "all" or "show-indexes" ? Enum.GetValues<VectorIndexType>() :
                [command switch { "ivf" => VectorIndexType.IVF, "hnsw" => VectorIndexType.HNSW, _ => VectorIndexType.DiskANN }];
            foreach (var type in types)
            {
                var store = new MongoDbService(client.GetDatabase(config.Database), config.Collection(type));
                if (command == "show-indexes")
                {
                    var catalog = await store.ListIndexesAsync(timeout.Token);
                    var verified = VectorSchema.CheckCatalog(catalog, VectorSchema.Index(type));
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { Algorithm = type.Suffix(), IndexCount = catalog.Count, IndexVerified = verified }));
                }
                else await output.WriteLineAsync(JsonSerializer.Serialize(await new VectorSearchService(store).RunAsync(type, fixture!, config.AllowWrites, timeout.Token)));
            }
            return 0;
        }
        catch (Exception ex)
        {
            var message = ex switch
            {
                ConfigurationException => "configuration rejected; check README guards and input files",
                IndexConflictException => "index conflict; preserved existing index and documents; use a new disposable prefix",
                VerificationException => "catalog or top-5 fixture self-match verification failed",
                OperationCanceledException or TimeoutException => "operation timed out or was canceled",
                _ => "operation failed; check credentials, permissions, network and service support"
            };
            // Never emit exception text, URI, credentials, server responses, or stack traces.
            await error.WriteLineAsync("ERROR: " + message);
            return 1;
        }
    }
}
