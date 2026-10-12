using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DocumentDBVectorSamples.Models;
using DocumentDBVectorSamples.Services;

namespace DocumentDBVectorSamples.Tests;

public class TemplateAndFixtureTests
{
    internal static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DocumentDBVectorSearch.csproj"))) root = root.Parent;
        return root?.FullName ?? throw new Exception("Repository root not found");
    }

    [Fact] public void ExampleIsSafeByDefaultAndMatchesRuntimeNames()
    {
        var text = File.ReadAllText(Path.Combine(Root(), ".env.example"));
        var values = text.Split('\n').Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1].Trim());
        Assert.Equal("false", values["ALLOW_DATABASE_WRITES"]);
        Assert.Equal("false", values["ALLOW_MODEL_CALLS"]);
        Assert.Equal("", values["MONGO_DATABASE"]);
        Assert.Equal("", values["MONGO_COLLECTION_PREFIX"]);
        Assert.Throws<ConfigurationException>(() => AppConfiguration.Read(key => values.GetValueOrDefault(key)));
        foreach (var name in values.Keys)
            Assert.Contains(name, string.Join("\n", Directory.GetFiles(Root(), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "tests" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Select(File.ReadAllText)));
    }

    [Fact] public void CiHasReadOnlyPermissionsAndNoCloudSecrets()
    {
        var ci = File.ReadAllText(Path.Combine(Root(), ".github/workflows/ci.yml"));
        Assert.Contains("contents: read", ci);
        Assert.Contains("persist-credentials: false", ci);
        Assert.Contains("Category!=Live", ci);
        Assert.Contains("--locked-mode", ci);
        Assert.Contains("check-vulnerabilities.py", ci);
        Assert.DoesNotContain("secrets.", ci);
        Assert.DoesNotContain("id-token:", ci);
        Assert.DoesNotContain("pull_request_target", ci);
    }

    [Fact] public void ParseFixtureGivesDeterministicIdsAndBsonDoubles()
    {
        var json = "[" + string.Join(",", Enumerable.Range(1, 50).Select(i => JsonSerializer.Serialize(new
            { HotelId = i.ToString(), HotelName = "synthetic", DescriptionVector = WorkflowTests.SyntheticFixture().Query }))) + "]";
        using var input = JsonDocument.Parse(json);
        var docs = Fixture.ParseDocuments(input.RootElement);
        Assert.Equal(50, docs.Count); Assert.Equal("dotnet-hotel-1", docs[0]["_id"]);
        Assert.All(docs[0][Fixture.VectorField].AsBsonArray, value => Assert.True(value.IsDouble));
    }

    [Fact] public void DuplicateFixtureIdsRejected()
    {
        var record = JsonSerializer.Serialize(new { HotelId = "duplicate", DescriptionVector = WorkflowTests.SyntheticFixture().Query });
        using var input = JsonDocument.Parse("[" + string.Join(',', Enumerable.Repeat(record, 50)) + "]");
        Assert.Throws<ConfigurationException>(() => Fixture.ParseDocuments(input.RootElement));
    }

    [Fact] public async Task TamperedFixtureHashRejectedBeforeQueryRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "dotnet-fixture-test-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "[]");
            await Assert.ThrowsAsync<ConfigurationException>(() => Fixture.LoadAsync(path, "nonexistent-query", default));
        }
        finally { File.Delete(path); }
    }

    [Fact] public async Task ModelTransportDoesNotFollowCredentialRedirects()
    {
        using var destination = new TcpListener(IPAddress.Loopback, 0); destination.Start();
        using var origin = new TcpListener(IPAddress.Loopback, 0); origin.Start();
        var target = ((IPEndPoint)destination.LocalEndpoint).Port;
        var port = ((IPEndPoint)origin.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var serving = Task.Run(async () =>
        {
            using var socket = await origin.AcceptTcpClientAsync(timeout.Token);
            using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{target}/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
        }, timeout.Token);
        using var handler = EmbeddingService.CreateHttpHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.Null(handler.ServerCertificateCustomValidationCallback);
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Authorization = new("Bearer", "unit-test-not-real");
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/", timeout.Token);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        await serving;
        Assert.False(destination.Pending());
    }
}
