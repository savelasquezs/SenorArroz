using System.Text.Json;
using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public sealed class WhatsAppFlowPayloadTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("\"50\"", 50)]
    [InlineData("1.5", null)]
    [InlineData("true", null)]
    [InlineData("null", null)]
    [InlineData("[]", null)]
    [InlineData("{}", null)]
    [InlineData("\"NaN\"", null)]
    [InlineData("2147483648", null)]
    public void IntegerRejectsInvalidTypesWithoutThrowing(string value, int? expected)
    {
        using var document = JsonDocument.Parse($"{{\"quantity\":{value}}}");
        Assert.Equal(expected, WhatsAppFlowPayload.Integer(document.RootElement, "quantity"));
    }

    [Fact]
    public void StoredCompletionNeverContainsTokenAndReplayRestoresOnlyCurrentToken()
    {
        const string response = """{"screen":"SUCCESS","data":{"extension_message_response":{"params":{"flow_token":"secret-token","message":"Pedido confirmado"}}}}""";
        var stored = WhatsAppFlowPayload.WithoutTokens(response);
        Assert.DoesNotContain("secret-token", stored);
        Assert.DoesNotContain("flow_token", stored);
        var replay = WhatsAppFlowPayload.RestoreCompletionToken(stored, "current-token");
        using var parsed = JsonDocument.Parse(replay);
        var parameters = parsed.RootElement.GetProperty("data").GetProperty("extension_message_response").GetProperty("params");
        Assert.Equal("current-token", parameters.GetProperty("flow_token").GetString());
        Assert.Equal("Pedido confirmado", parameters.GetProperty("message").GetString());
    }

    [Fact]
    public void WebhookRedactsTokensInsideEmbeddedResponseJson()
    {
        var webhook = JsonSerializer.Serialize(new { entry = new[] { new { interactive = new
        {
            nfm_reply = new { response_json = JsonSerializer.Serialize(new { flow_token = "private", message = "ok" }) }
        } } } });
        var stored = WhatsAppFlowPayload.WithoutTokens(webhook);
        Assert.DoesNotContain("private", stored);
        Assert.DoesNotContain("flow_token", stored);
        Assert.Contains("message", stored);
    }
}
