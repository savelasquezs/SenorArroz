using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.WhatsApp;

namespace SenorArroz.Tests;

public class WhatsAppMetaErrorHandlingTests
{
    [Theory]
    [InlineData("573001234567", "to", "recipient")]
    [InlineData("+57 (300) 123-4567", "to", "recipient")]
    [InlineData("CO.0123456789abCDEF", "recipient", "to")]
    [InlineData("CO.ENT.0123456789abCDEF", "recipient", "to")]
    public async Task CloudClient_SendsDestinationInExactlyOneField(
        string destination, string expectedField, string omittedField)
    {
        var handler = new CapturingHandler();
        var client = CreateClient(handler);
        const string phoneId = "phone-id";
        const string token = "token";
        const string message = "Hola";
        const string link = "https://example.com/menu.png";

        var sends = new (string Type, Func<Task<WhatsAppCloudSendResult>> Send)[]
        {
            ("text", () => client.SendTextMessageAsync(phoneId, token, destination, message)),
            ("interactive", () => client.SendUrlButtonMessageAsync(phoneId, token, destination, message, "Abrir", link)),
            ("interactive", () => client.SendReplyButtonsMessageAsync(phoneId, token, destination, message, [new("yes", "Sí")])),
            ("interactive", () => client.SendListMessageAsync(phoneId, token, destination, message, "Ver", [new("yes", "Sí", null)])),
            ("interactive", () => client.SendFlowMessageAsync(phoneId, token, destination, message, "Abrir", "flow-id", "flow-token", "START")),
            ("image", () => client.SendImageLinkMessageAsync(phoneId, token, destination, link, message)),
            ("document", () => client.SendMediaMessageAsync(phoneId, token, destination, "document", "media-id", message, "menu.pdf")),
            ("template", () => client.SendTemplateMessageAsync(phoneId, token, destination, "greeting", "es", [message]))
        };

        foreach (var (type, send) in sends)
        {
            Assert.True((await send()).Success);
            using var json = JsonDocument.Parse(handler.Body);
            var payload = json.RootElement;
            Assert.Equal(destination, payload.GetProperty(expectedField).GetString());
            Assert.False(payload.TryGetProperty(omittedField, out _));
            Assert.Equal(type, payload.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, payload.GetProperty(type).ValueKind);
        }
    }

    [Fact]
    public async Task CloudClient_HttpErrorPreservesStatusMessageAndBodyButRedactsToken()
    {
        const string token = "EA-secret-token";
        const string body = """{"error":{"message":"Temporary Meta outage","type":"OAuthException","code":2,"error_subcode":99},"access_token":"EA-secret-token"}""";
        var logger = new RecordingLogger<WhatsAppCloudClient>();
        var client = CreateClient(new ResponseHandler(HttpStatusCode.ServiceUnavailable, body), logger);

        var result = await client.SendTextMessageAsync("phone-id", token, "573001234567", "Hola");

        Assert.False(result.Success);
        Assert.Contains("Meta WhatsApp HTTP 503", result.ErrorMessage);
        Assert.Contains("Temporary Meta outage", result.ErrorMessage);
        Assert.Contains("code=2", result.ErrorMessage);
        Assert.Contains("error_subcode", result.ErrorMessage);
        Assert.Contains("[REDACTED]", result.ErrorMessage);
        Assert.DoesNotContain(token, result.ErrorMessage);
        Assert.Contains(logger.Messages, message => message.Contains("StatusCode=503") && message.Contains("Temporary Meta outage"));
        Assert.DoesNotContain(logger.Messages, message => message.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CloudClient_NonJsonHttpErrorPreservesProviderBody()
    {
        var client = CreateClient(new ResponseHandler(HttpStatusCode.BadGateway, "upstream proxy unavailable"));

        var result = await client.SendTextMessageAsync("phone-id", "token", "573001234567", "Hola");

        Assert.Equal("Meta WhatsApp HTTP 502: upstream proxy unavailable | body: upstream proxy unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task CloudClient_AuthenticationTemplateSendsCodeInBodyAndCopyButton()
    {
        var handler = new CapturingHandler();
        var client = CreateClient(handler);

        var result = await client.SendAuthenticationTemplateMessageAsync(
            "phone-id", "token", "573001234567", "customers_web_authentication", "es", "482193");

        Assert.True(result.Success);
        Assert.Contains("customers_web_authentication", handler.Body);
        using var json = JsonDocument.Parse(handler.Body);
        Assert.Equal("573001234567", json.RootElement.GetProperty("to").GetString());
        Assert.False(json.RootElement.TryGetProperty("recipient", out _));
        Assert.Contains("\"sub_type\":\"url\"", handler.Body);
        Assert.Equal(2, handler.Body.Split("482193").Length - 1);
    }

    [Theory]
    [InlineData(true, "Meta WhatsApp timeout:")]
    [InlineData(false, "Meta WhatsApp network_error:")]
    public async Task CloudClient_TransportFailureKeepsExplicitClassification(bool timeout, string expectedPrefix)
    {
        var exception = timeout
            ? (Exception)new TaskCanceledException("request timed out")
            : new HttpRequestException("connection reset");
        var client = CreateClient(new ThrowingHandler(exception));

        var result = await client.SendTextMessageAsync("phone-id", "token", "573001234567", "Hola");

        Assert.False(result.Success);
        Assert.StartsWith(expectedPrefix, result.ErrorMessage);
        Assert.True(result.OutcomeUncertain);
        Assert.Contains(timeout ? "request timed out" : "connection reset", result.ErrorMessage);
    }

    [Fact]
    public async Task CloudClient_TransportFailureDoesNotLogTokenFromException()
    {
        const string token = "EA-sensitive-token";
        var logger = new RecordingLogger<WhatsAppCloudClient>();
        var client = CreateClient(new ThrowingHandler(new HttpRequestException($"failed with access_token={token}")), logger);

        var result = await client.SendTextMessageAsync("phone-id", token, "573001234567", "Hola");

        Assert.DoesNotContain(token, result.ErrorMessage);
        Assert.Contains("access_token=[REDACTED]", result.ErrorMessage);
        Assert.DoesNotContain(logger.Messages, message => message.Contains(token, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(599)]
    public async Task AutomaticSender_ExplicitTransientHttpResponseIsRetrySafe(int statusCode)
    {
        var result = await SendWithError($"Meta WhatsApp HTTP {statusCode}: provider unavailable | body: failure");

        Assert.False(result.Success);
        Assert.True(result.TransientFailure);
    }

    [Theory]
    [InlineData("Meta WhatsApp HTTP 400: bad request | body: invalid parameter")]
    [InlineData("Meta WhatsApp HTTP 401: invalid token | body: unauthorized")]
    [InlineData("Meta WhatsApp HTTP 404: unknown phone | body: not found")]
    [InlineData("The order number is 500 but this is a permanent error")]
    [InlineData("Meta WhatsApp invalid_response: malformed JSON")]
    public async Task AutomaticSender_PermanentOrUnstructuredFailureIsNotRetrySafe(string error)
    {
        var result = await SendWithError(error);

        Assert.False(result.Success);
        Assert.False(result.TransientFailure);
    }

    [Theory]
    [InlineData("Meta WhatsApp timeout: request timed out")]
    [InlineData("Meta WhatsApp network_error: connection reset")]
    public async Task AutomaticSender_AmbiguousPostTransportFailureIsNotAutomaticallyRetried(string error)
    {
        var result = await SendWithError(error);

        Assert.False(result.Success);
        Assert.False(result.TransientFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task AutomaticSender_AlreadySending_DoesNotStartAnotherMetaPost()
    {
        var result = await SendWithError("should not be returned", WhatsAppAiProcessingStatus.Sending);

        Assert.False(result.Success);
        Assert.True(result.InProgress);
        Assert.False(result.TransientFailure);
        Assert.Contains("en curso", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static WhatsAppCloudClient CreateClient(HttpMessageHandler handler, ILogger<WhatsAppCloudClient>? logger = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new WhatsAppCloudOptions { BaseUrl = "https://graph.example", GraphApiVersion = "v20.0" }),
            logger ?? Mock.Of<ILogger<WhatsAppCloudClient>>());

    private static async Task<WhatsAppAutomaticSendResult> SendWithError(string error, WhatsAppAiProcessingStatus processingStatus = WhatsAppAiProcessingStatus.ResponseGenerated)
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            currentTenant: TestTenantContext.Default,
            tenantExecutionContext: TestTenantContext.Default);
        var branch = new Branch { Id = 1, Name = "Centro" };
        db.Branches.Add(branch);
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 1,
            BranchId = branch.Id,
            Branch = branch,
            PhoneNumber = "573001234567",
            AttentionMode = WhatsAppAttentionMode.Ai
        });
        db.WhatsAppMessages.Add(new WhatsAppMessage
        {
            Id = 10,
            ConversationId = 1,
            Direction = WhatsAppMessageDirection.Inbound,
            Type = WhatsAppMessageType.Text,
            TextBody = "Hola",
            Status = WhatsAppMessageStatus.Received,
            Timestamp = DateTime.UtcNow,
            AiProcessingStatus = processingStatus
        });
        db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting
        {
            Id = 1,
            BranchId = branch.Id,
            PhoneNumberId = "phone-id",
            AccessToken = "token",
            IsActive = true,
            IsVerified = true
        });
        await db.SaveChangesAsync();

        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendTextMessageAsync("phone-id", "token", "573001234567", "respuesta", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(false, null, error));
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);
        var sender = new WhatsAppAutomaticMessageSender(
            db,
            cloud.Object,
            Mock.Of<IWhatsAppNotificationService>(),
            clock.Object);

        return await sender.SendTextAsync(1, 10, "attempt", "respuesta", default);
    }

    private sealed class ResponseHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"messages\":[{\"id\":\"wamid.1\"}]}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
