using Azure.Core;
using DocumentDBVectorSamples.Models;
using DocumentDBVectorSamples.Services;
using DocumentDBVectorSamples.Utilities;

namespace DocumentDBVectorSamples.Tests;

public class SafetyTests
{
    internal const string Host = "example.global.mongocluster.cosmos.azure.com";
    internal static AppConfiguration Good => new("mongodb+srv://" + Host + "/?tls=true&retrywrites=false", Host,
        "dotnet_disposable_test", "fixture", "scram", "test-user", "not-a-real-password", "data", "query", true, 60);

    [Theory]
    [InlineData("")][InlineData("Hotels")][InlineData("admin")][InlineData("local")][InlineData("dotnet_")]
    [InlineData("dotnet_a.b")][InlineData("dotnet_x\n")][InlineData("dotnet_a/b")][InlineData("dotnet_$test")]
    public void UnsafeNamespaceRejected(string value) => Assert.Throws<ConfigurationException>(() => (Good with { Database = value }).Validate());

    [Theory]
    [InlineData("dotnet_disposable_20261012")][InlineData("sample_smoke")][InlineData("vectorsearch_disposable_batch2net_20261012")]
    public void DisposableNamespaceAccepted(string value) => (Good with { Database = value }).Validate();

    [Fact] public void NoDefaultNamespaceOrAuth() => Assert.Throws<ConfigurationException>(() => AppConfiguration.Read(_ => null));
    [Fact] public void WritesAreOptIn() => Assert.Throws<ConfigurationException>(() => (Good with { AllowWrites = false }).RequireWrites());
    [Theory][InlineData(0)][InlineData(29)][InlineData(601)]
    public void BoundedTimeout(int seconds) => Assert.Throws<ConfigurationException>(() => (Good with { TimeoutSeconds = seconds }).Validate());
    [Fact] public void NativeRequiresPassword() => Assert.Throws<ConfigurationException>(() => (Good with { Password = "" }).Validate());
    [Fact] public void OidcRejectsAccidentalNativePassword() => Assert.Throws<ConfigurationException>(() => (Good with { AuthMode = "oidc" }).Validate());

    [Theory]
    [InlineData("mongodb://example.global.mongocluster.cosmos.azure.com/")]
    [InlineData("mongodb+srv://user:secret@example.global.mongocluster.cosmos.azure.com/")]
    [InlineData("mongodb+srv://evil.example/")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com.evil.example/")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com:27017/")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/admin")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/?tls=false")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/?tlsAllowInvalidCertificates=true")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/?tlsInsecure=true")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/?authMechanism=PLAIN")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/?authSource=elsewhere")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/?tls=true&tls=false")]
    [InlineData("mongodb+srv://example.global.mongocluster.cosmos.azure.com/#secret")]
    public void MongoDestinationRejectedBeforeCredentialFactory(string uri)
    {
        var called = false;
        Assert.Throws<ConfigurationException>(() => ClientFactory.Settings(Good with { MongoUri = uri, AuthMode = "oidc", Password = "" },
            () => { called = true; throw new Exception("must not run"); }));
        Assert.False(called);
    }

    [Fact] public void ExactHostRequired() => Assert.Throws<ConfigurationException>(() => ClientFactory.Settings(Good with { AllowedHost = "other.global.mongocluster.cosmos.azure.com" }));

    [Fact] public void NativeSettingsAreStrictAndDoNotConstructAzureCredentials()
    {
        var settings = ClientFactory.Settings(Good, () => throw new Exception("must not run"));
        Assert.True(settings.UseTls);
        Assert.False(settings.AllowInsecureTls);
        Assert.False(settings.RetryWrites);
        Assert.Equal("SCRAM-SHA-256", settings.Credential.Mechanism);
        Assert.Equal("admin", settings.Credential.Source);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.ServerSelectionTimeout);
        Assert.Null(settings.SslSettings?.ServerCertificateValidationCallback);
    }

    [Fact] public void OidcUsesActualDriverCallbackAndExternalSource()
    {
        var calls = 0;
        var settings = ClientFactory.Settings(Good with { AuthMode = "oidc", Password = "" }, () => { calls++; return new FakeCredential(); });
        Assert.Equal(1, calls);
        Assert.Equal("MONGODB-OIDC", settings.Credential.Mechanism);
        Assert.Equal("$external", settings.Credential.Source);
        Assert.NotNull(settings.Credential.GetMechanismProperty<object>("OIDC_CALLBACK", null!));
    }

    [Theory]
    [InlineData("http://example.openai.azure.com/")][InlineData("https://evil.example/")]
    [InlineData("https://example.openai.azure.com.evil.example/")][InlineData("https://example.openai.azure.com:444/")]
    [InlineData("https://secret@example.openai.azure.com/")][InlineData("https://example.openai.azure.com/path")]
    [InlineData("https://example.openai.azure.com/?x=secret")][InlineData("https://example.openai.azure.com/#x")]
    public async Task ModelDestinationRejectedBeforeCredentialFactory(string endpoint)
    {
        var called = false;
        await Assert.ThrowsAsync<ConfigurationException>(() => EmbeddingService.CreateQueryAsync("test", endpoint, "example.openai.azure.com", "embedding", true, default,
            () => { called = true; throw new Exception("must not run"); }));
        Assert.False(called);
    }

    [Fact] public async Task ModelCallsAreSeparatelyOptIn() => await Assert.ThrowsAsync<ConfigurationException>(() =>
        EmbeddingService.CreateQueryAsync("test", "https://example.openai.azure.com/", "example.openai.azure.com", "embedding", false, default,
            () => throw new Exception("must not run")));

    [Fact] public async Task OidcTokenUsesDatabaseAudienceAndActualExpiry()
    {
        var credential = new FakeCredential();
        var callback = new AzureIdentityTokenHandler(credential);
        using var cts = new CancellationTokenSource();
        var sync = callback.GetOidcAccessToken(null!, cts.Token);
        var asyncToken = await callback.GetOidcAccessTokenAsync(null!, cts.Token);
        Assert.Equal("unit-test-token", sync.AccessToken);
        Assert.Equal(sync.AccessToken, asyncToken.AccessToken);
        Assert.InRange(asyncToken.ExpiresIn!.Value.TotalSeconds, 3590, 3600);
        Assert.Equal([AzureIdentityTokenHandler.Scope], credential.Scopes!);
        Assert.Equal(cts.Token, credential.CancellationToken);
        Assert.Equal(2, credential.Calls);
    }

    [Fact] public async Task ExpiredOidcTokenFails()
    {
        var callback = new AzureIdentityTokenHandler(new FakeCredential { Expiry = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Assert.Throws<InvalidOperationException>(() => callback.GetOidcAccessToken(null!, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => callback.GetOidcAccessTokenAsync(null!, default));
    }

    internal sealed class FakeCredential : TokenCredential
    {
        public DateTimeOffset Expiry { get; init; } = DateTimeOffset.UtcNow.AddHours(1);
        public string[]? Scopes { get; private set; }
        public CancellationToken CancellationToken { get; private set; }
        public int Calls { get; private set; }
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken cancellationToken)
        { Scopes = context.Scopes; CancellationToken = cancellationToken; Calls++; return new AccessToken("unit-test-token", Expiry); }
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken cancellationToken) => new(GetToken(context, cancellationToken));
    }
}
