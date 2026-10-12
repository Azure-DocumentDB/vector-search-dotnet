using System.Diagnostics;
using DocumentDBVectorSamples.Models;

namespace DocumentDBVectorSamples.Tests;

public class CliTests
{
    [Theory][InlineData("ivf")][InlineData("hnsw")][InlineData("diskann")][InlineData("all")][InlineData("show-indexes")][InlineData("embed-query")][InlineData("validate-fixture")]
    public async Task EveryEntrypointReturnsNonzeroAndRedactsRuntimeExceptions(string command)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var result = await Program.RunAsync([command], new StringReader(""), output, error, _ => null,
            (_, _) => throw new Exception("mongodb+srv://user:secret@host/?token=secret"));
        Assert.Equal(1, result);
        Assert.DoesNotContain("secret", output.ToString() + error);
        Assert.DoesNotContain("mongodb", error.ToString());
        Assert.Contains("ERROR:", error.ToString());
    }

    [Theory][InlineData("3", "ivf")][InlineData("4", "hnsw")][InlineData("5", "diskann")][InlineData("2", "show-indexes")][InlineData("1", "embed-query")]
    public async Task MenuRoutesToSameFailureHandler(string input, string expected)
    {
        string? seen = null;
        var result = await Program.RunAsync([], new StringReader(input), new StringWriter(), new StringWriter(), _ => null,
            (command, _) => { seen = command; throw new TimeoutException("private URI"); });
        Assert.Equal(1, result); Assert.Equal(expected, seen);
    }

    [Theory][InlineData("")][InlineData("invalid\n")][InlineData("0")]
    public async Task MenuEofExitsWithoutConfigurationOrNetwork(string input)
    {
        var result = await Program.RunAsync([], new StringReader(input), new StringWriter(), new StringWriter(),
            _ => throw new Exception("environment must not be read"), (_, _) => throw new Exception("must not execute"));
        Assert.Equal(0, result);
    }

    [Fact] public async Task UnknownArgumentsAreNotEchoed()
    {
        using var error = new StringWriter();
        Assert.Equal(1, await Program.RunAsync(["secret"], new StringReader(""), new StringWriter(), error));
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Theory][InlineData("ivf")][InlineData("hnsw")][InlineData("diskann")][InlineData("all")][InlineData("show-indexes")][InlineData("embed-query")][InlineData("validate-fixture")]
    public async Task RealProcessMissingConfigurationFails(string command)
    {
        var (exit, text) = await SpawnAsync([command]);
        Assert.Equal(1, exit); Assert.Contains("ERROR:", text);
        Assert.DoesNotContain("secret-canary", text);
    }

    [Fact] public async Task RealProcessEofDoesNotHang()
    {
        var (exit, _) = await SpawnAsync([]); Assert.Equal(0, exit);
    }

    private static async Task<(int, string)> SpawnAsync(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var psi = new ProcessStartInfo(host) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("MONGO_", StringComparison.Ordinal) ||
            k.StartsWith("AZURE_", StringComparison.Ordinal) || k.StartsWith("ALLOW_", StringComparison.Ordinal)).ToArray()) psi.Environment.Remove(key);
        psi.Environment["MONGO_URI"] = "mongodb+srv://user:secret-canary@untrusted.example/";
        using var process = Process.Start(psi)!;
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
