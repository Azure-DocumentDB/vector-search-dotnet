using Azure.Core;
using Azure.Identity;
using DocumentDBVectorSamples.Models;
using MongoDB.Driver;

namespace DocumentDBVectorSamples.Utilities;

public static class ClientFactory
{
    public static TokenCredential CreateCredential() => new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ExcludeInteractiveBrowserCredential = true
    });

    // Validate the complete destination BEFORE invoking credential factories or constructing clients.
    public static Uri ValidateMongoDestination(string value, string allowedHost)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "mongodb+srv" ||
            uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.AbsolutePath is not ("" or "/") ||
            uri.Fragment.Length != 0 || !IsAzureHost(uri.Host, ".mongocluster.cosmos.azure.com") ||
            !string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("Use a credential-free Azure DocumentDB SRV URI and its exact MONGO_ALLOWED_HOST.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !keys.Add(pair[0]) ||
                !(pair[0].Equals("tls", StringComparison.OrdinalIgnoreCase) && pair[1] == "true" ||
                  pair[0].Equals("retrywrites", StringComparison.OrdinalIgnoreCase) && pair[1] == "false" ||
                  pair[0].Equals("authsource", StringComparison.OrdinalIgnoreCase) && pair[1] == "admin"))
                throw new ConfigurationException("Unsupported URI option. Only tls=true, retrywrites=false and authSource=admin are accepted.");
        }
        return uri;
    }

    public static Uri ValidateOpenAiDestination(string endpoint, string allowedHost)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/" ||
            !IsAzureHost(uri.Host, ".openai.azure.com") || !string.Equals(uri.Host, allowedHost, StringComparison.OrdinalIgnoreCase))
            throw new ConfigurationException("Use an HTTPS Azure OpenAI root endpoint and its exact AZURE_OPENAI_ALLOWED_HOST.");
        return uri;
    }

    private static bool IsAzureHost(string host, string suffix) =>
        host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length &&
        host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    public static MongoClientSettings Settings(AppConfiguration config, Func<TokenCredential>? credentialFactory = null)
    {
        config.Validate();
        ValidateMongoDestination(config.MongoUri, config.AllowedHost);
        var settings = MongoClientSettings.FromConnectionString(config.MongoUri);
        settings.UseTls = true;
        settings.AllowInsecureTls = false;
        settings.RetryWrites = false;
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(15);
        settings.ConnectTimeout = TimeSpan.FromSeconds(10);
        settings.SocketTimeout = TimeSpan.FromSeconds(30);
        settings.WaitQueueTimeout = TimeSpan.FromSeconds(15);
        settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(2);
        settings.MaxConnectionPoolSize = 10;
        settings.ApplicationName = "documentdb-vector-dotnet";
        settings.Credential = config.AuthMode == "scram"
            ? new MongoCredential("SCRAM-SHA-256", new MongoInternalIdentity("admin", config.Username), new PasswordEvidence(config.Password))
            : MongoCredential.CreateOidcCredential(new AzureIdentityTokenHandler((credentialFactory ?? CreateCredential)()));
        return settings;
    }
}
