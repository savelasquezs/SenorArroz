using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SenorArroz.API.Security;

public sealed class StorefrontApiKeyOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "StorefrontApiKey";
    public string KeyId { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
}

public sealed class StorefrontApiKeyValidator(IOptions<StorefrontApiKeyOptions> options)
{
    public bool IsValid(string? keyId, string? key)
    {
        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.KeyId)
            || string.IsNullOrWhiteSpace(configured.KeyHash)
            || string.IsNullOrWhiteSpace(keyId)
            || string.IsNullOrWhiteSpace(key))
            return false;

        byte[] expectedKeyHash;
        try
        {
            expectedKeyHash = Convert.FromHexString(configured.KeyHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var suppliedIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(keyId));
        var configuredIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured.KeyId));
        var suppliedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return expectedKeyHash.Length == suppliedKeyHash.Length
            && CryptographicOperations.FixedTimeEquals(configuredIdHash, suppliedIdHash)
            && CryptographicOperations.FixedTimeEquals(expectedKeyHash, suppliedKeyHash);
    }
}

public sealed class StorefrontApiKeyAuthenticationHandler(
    IOptionsMonitor<StorefrontApiKeyOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    StorefrontApiKeyValidator validator)
    : AuthenticationHandler<StorefrontApiKeyOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var keyId = Request.Headers["X-Storefront-Key-Id"].FirstOrDefault();
        var key = Request.Headers["X-Storefront-Key"].FirstOrDefault();
        if (!validator.IsValid(keyId, key))
            return Task.FromResult(AuthenticateResult.Fail("Credenciales de storefront inválidas."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, keyId!)],
            StorefrontApiKeyOptions.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), StorefrontApiKeyOptions.Scheme)));
    }
}
