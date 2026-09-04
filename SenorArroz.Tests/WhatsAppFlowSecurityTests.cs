using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.WhatsApp;

namespace SenorArroz.Tests;

public sealed class WhatsAppFlowSecurityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlowSendDoesNotExposeProviderPayloadOrUseNavigateParameters(bool networkFailure)
    {
        const string sensitive = "private-flow-token private-address private-phone";
        var handler = new FlowResponseHandler(sensitive, networkFailure);
        using var http = new HttpClient(handler);
        var logger = new FlowLogger();
        var client = new WhatsAppCloudClient(http, Options.Create(new WhatsAppCloudOptions()), logger);

        var result = await client.SendFlowMessageAsync("123", "test-access-token", "573000000000",
            "Tu pedido", "Comprar", "456", sensitive, "FULFILLMENT");

        Assert.False(result.Success);
        Assert.DoesNotContain(sensitive, result.ErrorMessage ?? string.Empty);
        Assert.All(logger.Messages, message => Assert.DoesNotContain(sensitive, message));
        using var document = JsonDocument.Parse(handler.Payload!);
        var parameters = document.RootElement.GetProperty("interactive").GetProperty("action").GetProperty("parameters");
        Assert.Equal("data_exchange", parameters.GetProperty("flow_action").GetString());
        Assert.False(parameters.TryGetProperty("flow_action_payload", out _));
        Assert.Equal(sensitive, parameters.GetProperty("flow_token").GetString());
    }

    private sealed class FlowResponseHandler(string sensitive, bool networkFailure) : HttpMessageHandler
    {
        public string? Payload { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Payload = await request.Content!.ReadAsStringAsync(ct);
            if (networkFailure) throw new HttpRequestException(sensitive);
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { error = new { message = sensitive } }))
            };
        }
    }

    private sealed class FlowLogger : ILogger<WhatsAppCloudClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Theory]
    [InlineData("pedido")]
    [InlineData("  Hacer   PEDIDO ")]
    [InlineData("ver menú")]
    [InlineData("comprar")]
    public void PurchaseIntentRecognizesOnlyDeterministicCommands(string value) =>
        Assert.True(WhatsAppCommerceFlowService.IsPurchaseIntent(value));

    [Theory]
    [InlineData("¿Cuándo abren?")]
    [InlineData("Quiero saber dónde están")]
    [InlineData("pedido de ayer")]
    public void PurchaseIntentLeavesOtherMessagesForAi(string value) =>
        Assert.False(WhatsAppCommerceFlowService.IsPurchaseIntent(value));

    [Fact]
    public void CryptoUsesOaepSha256AndFlippedResponseIv()
    {
        using var rsa = RSA.Create(2048);
        var service = new WhatsAppFlowCrypto(Options.Create(new WhatsAppFlowOptions
        {
            PrivateKey = rsa.ExportPkcs8PrivateKeyPem()
        }));
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        const string json = "{\"version\":\"3.0\",\"action\":\"ping\"}";
        var encryptedKey = rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
        var requestData = Encrypt(json, key, iv);

        var decrypted = service.Decrypt(new WhatsAppEncryptedFlowRequest(
            Convert.ToBase64String(encryptedKey), Convert.ToBase64String(requestData), Convert.ToBase64String(iv)));
        Assert.Equal(json, decrypted.Json);

        var response = service.Encrypt("{\"ok\":true}", decrypted.AesKey, decrypted.InitialVector);
        var flippedIv = iv.Select(x => (byte)~x).ToArray();
        Assert.Equal("{\"ok\":true}", Decrypt(Convert.FromBase64String(response), key, flippedIv));
    }

    [Fact]
    public void CryptoRejectsAlteredCiphertext()
    {
        using var rsa = RSA.Create(2048);
        var service = new WhatsAppFlowCrypto(Options.Create(new WhatsAppFlowOptions { PrivateKey = rsa.ExportPkcs8PrivateKeyPem() }));
        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        var encrypted = Encrypt("{}", key, iv);
        encrypted[0] ^= 0xff;
        Assert.Throws<CryptographicException>(() => service.Decrypt(new(
            Convert.ToBase64String(rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256)),
            Convert.ToBase64String(encrypted), Convert.ToBase64String(iv))));
    }

    private static byte[] Encrypt(string value, byte[] key, byte[] iv)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(true, new AeadParameters(new KeyParameter(key), 128, iv));
        var output = new byte[cipher.GetOutputSize(plain.Length)];
        var length = cipher.ProcessBytes(plain, 0, plain.Length, output, 0);
        length += cipher.DoFinal(output, length);
        return output.AsSpan(0, length).ToArray();
    }

    private static string Decrypt(byte[] value, byte[] key, byte[] iv)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(false, new AeadParameters(new KeyParameter(key), 128, iv));
        var plain = new byte[cipher.GetOutputSize(value.Length)];
        var length = cipher.ProcessBytes(value, 0, value.Length, plain, 0);
        length += cipher.DoFinal(plain, length);
        return Encoding.UTF8.GetString(plain, 0, length);
    }
}
