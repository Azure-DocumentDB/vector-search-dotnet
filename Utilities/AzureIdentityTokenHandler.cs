using Azure.Core;
using MongoDB.Driver.Authentication.Oidc;

namespace DocumentDBVectorSamples.Utilities;

/// <summary>Driver OIDC callback; returns the real token and remaining lifetime, never logs it.</summary>
public sealed class AzureIdentityTokenHandler(TokenCredential credential) : IOidcCallback
{
    public const string Scope = "https://ossrdbms-aad.database.windows.net/.default";
    public OidcAccessToken GetOidcAccessToken(OidcCallbackParameters parameters, CancellationToken cancellationToken) =>
        Convert(credential.GetToken(new TokenRequestContext([Scope]), cancellationToken));

    public async Task<OidcAccessToken> GetOidcAccessTokenAsync(OidcCallbackParameters parameters, CancellationToken cancellationToken) =>
        Convert(await credential.GetTokenAsync(new TokenRequestContext([Scope]), cancellationToken));

    private static OidcAccessToken Convert(AccessToken token)
    {
        var remaining = token.ExpiresOn - DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(token.Token) || remaining <= TimeSpan.FromSeconds(30))
            throw new InvalidOperationException("No usable database token.");
        return new OidcAccessToken(token.Token, remaining);
    }
}
