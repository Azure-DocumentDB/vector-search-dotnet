using System.Text.RegularExpressions;

namespace DocumentDBVectorSamples.Models;

public sealed class ConfigurationException(string message) : Exception(message);
public sealed class IndexConflictException : Exception;
public sealed class DuplicateDocumentException : Exception;
public sealed class VerificationException : Exception;

/// <summary>Explicit process configuration. No secrets/default namespaces in checked-in files.</summary>
public sealed record AppConfiguration(
    string MongoUri, string AllowedHost, string Database, string CollectionPrefix,
    string AuthMode, string Username, string Password,
    string DataFile, string QueryFile, bool AllowWrites, int TimeoutSeconds)
{
    public static AppConfiguration Read(Func<string, string?>? environment = null)
    {
        var get = environment ?? Environment.GetEnvironmentVariable;
        string Value(string key, string fallback = "") => get(key) ?? fallback;
        var config = new AppConfiguration(
            Value("MONGO_URI"), Value("MONGO_ALLOWED_HOST"), Value("MONGO_DATABASE"),
            Value("MONGO_COLLECTION_PREFIX"), Value("MONGO_AUTH_MODE"),
            Value("MONGO_USERNAME"), Value("MONGO_PASSWORD"),
            Value("VECTOR_DATA_FILE", "data/Hotels_Vector.json"), Value("QUERY_VECTOR_FILE", "data/query-vector.json"),
            Value("ALLOW_DATABASE_WRITES") == "true",
            int.TryParse(Value("OPERATION_TIMEOUT_SECONDS", "300"), out var seconds) ? seconds : 0);
        config.Validate();
        return config;
    }

    public void Validate()
    {
        // Intent guards, not proof a namespace contains no valuable data.
        ValidateNamespace(Database);
        if (!Regex.IsMatch(CollectionPrefix, @"\A[a-z][a-z0-9_]{0,47}\z", RegexOptions.CultureInvariant))
            throw new ConfigurationException("Set an explicit lowercase MONGO_COLLECTION_PREFIX.");
        if (AuthMode is not ("scram" or "oidc")) throw new ConfigurationException("Set MONGO_AUTH_MODE to scram or oidc.");
        if (AuthMode == "scram" && (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password)))
            throw new ConfigurationException("Native authentication requires MONGO_USERNAME and MONGO_PASSWORD.");
        if (AuthMode == "oidc" && !string.IsNullOrEmpty(Password))
            throw new ConfigurationException("Unset MONGO_PASSWORD for OIDC (the live gate isolates native credentials).");
        if (TimeoutSeconds is < 30 or > 600) throw new ConfigurationException("OPERATION_TIMEOUT_SECONDS must be 30..600.");
        if (string.IsNullOrWhiteSpace(DataFile) || string.IsNullOrWhiteSpace(QueryFile))
            throw new ConfigurationException("Provide both fixture paths.");
    }

    public static void ValidateNamespace(string name)
    {
        if (name.Length > 63 || !Regex.IsMatch(name, @"\A(dotnet_|sample_|vectorsearch_disposable_)[a-z0-9_]{1,48}\z", RegexOptions.CultureInvariant))
            throw new ConfigurationException("Use an explicit disposable dotnet_, sample_ or vectorsearch_disposable_ database.");
    }

    public void RequireWrites()
    {
        if (!AllowWrites) throw new ConfigurationException("Set ALLOW_DATABASE_WRITES=true only for an approved disposable namespace.");
    }

    public string Collection(VectorIndexType type) => $"{CollectionPrefix}_{type.Suffix()}";
}
