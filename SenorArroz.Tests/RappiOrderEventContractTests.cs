using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

[CollectionDefinition("Rappi provider token cache", DisableParallelization = true)]
public sealed class RappiProviderTokenCacheCollection;

[Collection("Rappi provider token cache")]
public sealed class RappiOrderEventContractTests
{
    // Exact public PAv2 example; no customer/order credentials or captured data.
    internal const string OfficialExample = """
        [{"event":"canceled_with_charge","event_time":"2020-05-28T12:31:12.501Z","additional_information":{}},
         {"event":"taken_visible_order","event_time":"2020-05-28T12:30:12.501Z","additional_information":{"eta_to_store":"15"}}]
        """;

    [Fact]
    public async Task Official_array_retains_typed_events_UTC_and_detached_additional_information()
    {
        var (provider, handler) = Create(OfficialExample);
        var result = await provider.GetOrderEventsAsync("synthetic order", default);
        Assert.True(result.Success);
        Assert.Equal(2, result.Events!.Count);
        Assert.Equal("canceled_with_charge", result.Events[0].Event);
        Assert.Equal(DateTimeOffset.Parse("2020-05-28T12:31:12.501Z"), result.Events[0].EventTime);
        Assert.Equal(TimeSpan.Zero, result.Events[0].EventTime.Offset);
        Assert.Equal("15", result.Events[1].AdditionalInformation!.Value.GetProperty("eta_to_store").GetString());
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.EndsWith("/orders/synthetic%20order/events", handler.Path);
        Assert.StartsWith("bearer ", handler.Authorization);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"events\":[]}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("[\"taken_visible_order\"]")]
    [InlineData("[{\"event\":\"taken_visible_order\"}]")]
    [InlineData("[{\"event\":1,\"event_time\":\"2020-05-28T12:30:12.501Z\"}]")]
    [InlineData("[{\"event\":\"\",\"event_time\":\"2020-05-28T12:30:12.501Z\"}]")]
    [InlineData("[{\"event\":\"taken_visible_order\",\"event_time\":\"2020-05-28T12:30:12.501\"}]")]
    [InlineData("[{\"event\":\"taken_visible_order\",\"event_time\":\"2020-05-28T13:30:12.501+01:00\"}]")]
    [InlineData("[{\"event\":\"taken_visible_order\",\"event_time\":\"yesterday\"}]")]
    [InlineData("[{\"event\":\"cancel_by_user\",\"event\":\"taken_visible_order\",\"event_time\":\"2020-05-28T12:30:12.501Z\"}]")]
    [InlineData("[{\"event\":\"taken_visible_order\",\"event_time\":\"2020-05-28T12:30:12.501Z\",\"event_time\":\"2020-05-28T12:31:12.501Z\"}]")]
    public async Task Invalid_or_ambiguous_contract_is_not_successful_evidence(string body)
    {
        var result = await Create(body).Provider.GetOrderEventsAsync("synthetic", default);
        Assert.False(result.Success);
        Assert.Null(result.Events);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Unknown_events_and_additional_information_shapes_are_preserved_not_interpreted(string info)
    {
        var result = await Create("[{\"event\":\"future_event\",\"event_time\":\"2020-05-28T12:30:12.501Z\",\"additional_information\":" + info + "}]")
            .Provider.GetOrderEventsAsync("synthetic", default);
        Assert.True(result.Success);
        Assert.Equal("future_event", Assert.Single(result.Events!).Event);
        Assert.Equal(info, result.Events![0].AdditionalInformation!.Value.GetRawText());
    }

    [Fact]
    public async Task Empty_history_is_valid_but_not_acceptance_evidence() =>
        Assert.Empty((await Create("[]").Provider.GetOrderEventsAsync("synthetic", default)).Events!);

    private static (RappiDeliveryProvider Provider, EventHandler Handler) Create(string body)
    {
        var handler = new EventHandler(body);
        return (new(new HttpClient(handler), Options.Create(new RappiOptions
        {
            AuthUrl = "https://synthetic.invalid/token/login/integrations",
            ApiBaseUrl = "https://synthetic.invalid/api/v2/restaurants-integrations-public-api",
            ClientId = "synthetic", ClientSecret = "synthetic"
        })), handler);
    }

    private sealed class EventHandler(string body) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token/login/integrations"))
                return Task.FromResult(Json("""{"access_token":"synthetic-token","expires_in":3600}"""));
            Method = request.Method;
            Path = request.RequestUri.AbsolutePath;
            Authorization = request.Headers.GetValues("x-authorization").Single();
            return Task.FromResult(Json(body));
        }
        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
            { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
