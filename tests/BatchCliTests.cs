using System.Text.Json;

namespace DocumentDBVectorSamples.Tests;

public class BatchCliTests
{
    [Theory][InlineData("ivf")][InlineData("hnsw")]
    public async Task AllContinuesAfterFirstOrMiddleFailureWithThreeRowsAndNoLeaks(string failing)
    {
        var attempts = new List<string>();
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await Program.RunAsync(["all"], new StringReader(""), output, error, _ => null,
            (algorithm, _) =>
            {
                attempts.Add(algorithm);
                if (algorithm == failing) throw new Exception("mongodb+srv://secret-canary@private.example/?token=secret-canary");
                return Task.CompletedTask;
            });
        Assert.Equal(1, exit);
        Assert.Equal(["ivf", "hnsw", "diskann"], attempts);
        var rows = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.Equal(3, rows.Length);
            foreach (var row in rows)
            {
                var algorithm = row.RootElement.GetProperty("Algorithm").GetString();
                Assert.Equal(algorithm == failing ? "FAIL" : "PASS", row.RootElement.GetProperty("Status").GetString());
            }
        }
        finally { foreach (var row in rows) row.Dispose(); }
        Assert.DoesNotContain("secret-canary", output.ToString() + error);
        Assert.DoesNotContain("mongodb", output.ToString() + error);
    }

    [Fact] public async Task AllSuccessReturnsZeroWithThreeRows()
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await Program.RunAsync(["all"], new StringReader(""), output, error, _ => null,
            (_, _) => Task.CompletedTask);
        Assert.Equal(0, exit); Assert.Equal(3, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal("", error.ToString());
    }

    [Fact] public async Task TimedOutFirstCaseDoesNotCancelLaterCases()
    {
        var attempts = new List<VectorIndexType>();
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await Program.RunCasesAsync(Enum.GetValues<VectorIndexType>(), async (type, ct) =>
        {
            attempts.Add(type);
            Assert.False(ct.IsCancellationRequested);
            if (type == VectorIndexType.IVF) await Task.Delay(Timeout.Infinite, ct);
            return new { Algorithm = type.Suffix(), Status = "PASS" };
        }, TimeSpan.FromMilliseconds(100), output, error);
        Assert.Equal(1, exit); Assert.Equal(3, attempts.Count);
        Assert.Equal(3, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("timed out", error.ToString());
    }

    [Fact] public async Task SharedCancellationExplicitlyReportsAllRemainingCases()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await Program.RunCasesAsync(Enum.GetValues<VectorIndexType>(), (_, _) => throw new Exception("must not execute"),
            TimeSpan.FromSeconds(30), output, error, cts.Token);
        Assert.Equal(1, exit); Assert.Equal(3, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.DoesNotContain("must not execute", output.ToString() + error);
        Assert.Equal(3, output.ToString().Split("FAIL").Length - 1);
    }
}
