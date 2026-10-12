using Azure.AI.OpenAI;
using Azure.Core;
using DocumentDBVectorSamples.Models;
using DocumentDBVectorSamples.Utilities;
using System.ClientModel.Primitives;

namespace DocumentDBVectorSamples.Services;

/// <summary>Optional billable query embedding. Not called by any precomputed smoke workflow.</summary>
public static class EmbeddingService
{
    public static HttpClientHandler CreateHttpHandler() => new() { AllowAutoRedirect = false };

    public static async Task<double[]> CreateQueryAsync(string text, string endpoint, string allowedHost,
        string deployment, bool allowModelCalls, CancellationToken ct, Func<TokenCredential>? credentialFactory = null)
    {
        if (!allowModelCalls) throw new ConfigurationException("Model calls require ALLOW_MODEL_CALLS=true.");
        var destination = ClientFactory.ValidateOpenAiDestination(endpoint, allowedHost);
        if (string.IsNullOrWhiteSpace(deployment) || deployment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ConfigurationException("Set a valid AZURE_OPENAI_EMBEDDING_DEPLOYMENT.");
        if (string.IsNullOrWhiteSpace(text)) throw new ConfigurationException("Provide nonempty query text.");
        using var handler = CreateHttpHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var options = new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) };
        var client = new AzureOpenAIClient(destination, (credentialFactory ?? ClientFactory.CreateCredential)(), options);
        var response = await client.GetEmbeddingClient(deployment).GenerateEmbeddingAsync(text, cancellationToken: ct);
        var vector = response.Value.ToFloats().ToArray().Select(x => (double)x).ToArray();
        Fixture.ValidateVector(vector);
        return vector;
    }
}
