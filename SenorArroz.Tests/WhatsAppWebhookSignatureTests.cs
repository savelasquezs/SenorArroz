using System.Security.Cryptography;
using System.Text;
using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public sealed class WhatsAppWebhookSignatureTests
{
    [Fact]
    public void AcceptsValidMetaSignature()
    {
        const string secret = "app-secret";
        const string payload = "{\"object\":\"whatsapp_business_account\"}";
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        Assert.True(WhatsAppWebhookSignature.IsValid($"sha256={Convert.ToHexString(digest).ToLowerInvariant()}", payload, secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=invalid")]
    [InlineData("md5=0011")]
    public void RejectsInvalidSignature(string? signature) =>
        Assert.False(WhatsAppWebhookSignature.IsValid(signature, "{}", "secret"));
}
