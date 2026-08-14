using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Integrations;

namespace SenorArroz.Tests;

public sealed class RappiDeliveryProviderTests
{
    [Fact]
    public async Task Provider_UsesValidatedSandboxContracts()
    {
        var handler = new ContractHandler();
        var provider = new RappiDeliveryProvider(
            new HttpClient(handler),
            Options.Create(new RappiOptions
            {
                AuthUrl =
                    "https://api.dev.rappi.com/restaurants/auth/v1/token/login/integrations",
                ApiBaseUrl =
                    "https://api.dev.rappi.com/api/v2/restaurants-integrations-public-api",
                ClientId = "client-id",
                ClientSecret = "client-secret"
            }));

        var connection = await provider.TestConnectionAsync(CancellationToken.None);
        var repeatedConnection = await provider.TestConnectionAsync(CancellationToken.None);
        var storeIntegrated = await provider.SetStoreIntegratedAsync(
            "900173116",
            true,
            CancellationToken.None);
        var storeNotIntegrated = await provider.SetStoreIntegratedAsync(
            "900173117",
            false,
            CancellationToken.None);
        var menu = await provider.PublishMenuAsync(
            new RappiMenuRequest(
                "900173116",
                [
                    new RappiMenuItem(
                        new RappiMenuCategory("category-7", 0, 0, "Arroces", 0),
                        [],
                        "Arroz de prueba",
                        "Descripción",
                        25000,
                        "product-9",
                        0,
                        "PRODUCT",
                        "https://example.test/product.jpg")
                ]),
            CancellationToken.None);
        var availability = await provider.SetAvailabilityAsync(
            [
                new RappiAvailabilityRequest(
                    "900173116",
                    ["product-9"],
                    ["product-10"])
            ],
            CancellationToken.None);
        var webhook = await provider.ConfigureWebhookAsync(
            "NEW_ORDER",
            "https://senorarrozapi.up.railway.app/api/integrations/rappi/webhooks/id/NEW_ORDER",
            ["900173116", "900173117"],
            CancellationToken.None);
        var configuredWebhook = await provider.GetWebhookAsync(
            "NEW_ORDER",
            CancellationToken.None);
        var rotatedWebhook = await provider.ResetWebhookSecretAsync(
            "NEW_ORDER",
            CancellationToken.None);
        var menuApproval = await provider.GetMenuApprovalAsync(
            "900173116",
            CancellationToken.None);
        var rejection = await provider.RejectOrderAsync(
            "rappi-order-1",
            "Sin inventario",
            CancellationToken.None);

        Assert.True(connection.Success);
        Assert.True(repeatedConnection.Success);
        Assert.Equal("900173116", connection.Stores![0].StoreId);
        Assert.Equal("900173116", connection.Stores[0].IntegrationId);
        Assert.True(storeIntegrated.Success);
        Assert.True(storeNotIntegrated.Success);
        Assert.True(menu.Success);
        Assert.True(availability.Success);
        Assert.True(webhook.Success);
        Assert.True(configuredWebhook.Success);
        Assert.True(rotatedWebhook.Success);
        Assert.Equal(["900173116", "900173117"], configuredWebhook.EnabledStoreIds);
        Assert.True(menuApproval.Success);
        Assert.True(rejection.Success);
        Assert.Equal("webhook-secret", webhook.Secret);
        Assert.Equal("rotated-webhook-secret", rotatedWebhook.Secret);
        Assert.Equal(1, handler.AuthRequests);
        Assert.All(
            handler.ApiRequests,
            request => Assert.Equal("bearer sandbox-token", request.Authorization));

        using var tokenBody = JsonDocument.Parse(
            handler.Requests.Single(x => x.Path.EndsWith("/token/login/integrations")).Body);
        Assert.Equal("client-id", tokenBody.RootElement.GetProperty("client_id").GetString());
        Assert.Equal("client-secret", tokenBody.RootElement.GetProperty("client_secret").GetString());
        Assert.False(tokenBody.RootElement.TryGetProperty("audience", out _));
        Assert.False(tokenBody.RootElement.TryGetProperty("grant_type", out _));

        using var menuBody = JsonDocument.Parse(handler.Requests.Single(x => x.Path.EndsWith("/menu")).Body);
        Assert.Equal("900173116", menuBody.RootElement.GetProperty("storeId").GetString());
        var menuItem = menuBody.RootElement.GetProperty("items")[0];
        Assert.Equal("product-9", menuItem.GetProperty("sku").GetString());
        Assert.Equal("category-7", menuItem.GetProperty("category").GetProperty("id").GetString());
        Assert.Equal(0, menuItem.GetProperty("category").GetProperty("maxQty").GetInt32());
        Assert.Equal(0, menuItem.GetProperty("children").GetArrayLength());

        using var availabilityBody = JsonDocument.Parse(
            handler.Requests.Single(x => x.Path.EndsWith("/availability/stores/items")).Body);
        Assert.Equal(
            "900173116",
            availabilityBody.RootElement[0].GetProperty("store_integration_id").GetString());
        Assert.Equal(
            "product-9",
            availabilityBody.RootElement[0].GetProperty("items").GetProperty("turn_on")[0].GetString());

        using var webhookBody = JsonDocument.Parse(
            handler.Requests.Single(x => x.Path.EndsWith("/webhook")).Body);
        Assert.Equal("NEW_ORDER", webhookBody.RootElement.GetProperty("event").GetString());
        Assert.Equal(2, webhookBody.RootElement.GetProperty("data")[0].GetProperty("stores").GetArrayLength());

        using var rejectionBody = JsonDocument.Parse(
            handler.Requests.Single(x => x.Path.EndsWith("/orders/rappi-order-1/reject")).Body);
        Assert.Equal("Sin inventario", rejectionBody.RootElement.GetProperty("reason").GetString());
        var storeStatus = handler.Requests.Single(x =>
            x.Path.EndsWith("/stores-pa/900173116/status"));
        Assert.Equal("?integrated=true", storeStatus.Query);
        var disabledStoreStatus = handler.Requests.Single(x =>
            x.Path.EndsWith("/stores-pa/900173117/status"));
        Assert.Equal("?integrated=false", disabledStoreStatus.Query);
    }

    private sealed class ContractHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        public IEnumerable<CapturedRequest> ApiRequests =>
            Requests.Where(x => !x.Path.EndsWith("/token/login/integrations"));
        public int AuthRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var authorization = request.Headers.TryGetValues("x-authorization", out var values)
                ? values.Single()
                : null;
            Requests.Add(new(path, request.RequestUri.Query, body, authorization));

            if (path.EndsWith("/token/login/integrations"))
            {
                AuthRequests++;
                return Json(
                    """{"access_token":"sandbox-token","expires_in":3600,"token_type":"Bearer"}""");
            }

            if (path.EndsWith("/stores-pa"))
                return Json(
                    """[{"integrationId":"900173116","rappiId":"900173116","name":"Señor Arroz Dev1"},{"integrationId":"900173117","rappiId":"900173117","name":"Señor Arroz Dev2"}]""");
            if (path.EndsWith("/webhook"))
                return Json("""{"event":"NEW_ORDER","secret":"webhook-secret"}""");
            if (path.EndsWith("/webhook/NEW_ORDER/reset-secret"))
                return Json(
                    """{"event":"NEW_ORDER","stores":[{"store_id":"900173116","state":"ENABLE"},{"store_id":"900173117","state":"ENABLE"}],"secret":"rotated-webhook-secret"}""");
            if (path.EndsWith("/webhook/NEW_ORDER"))
                return Json(
                    """[{"event":"NEW_ORDER","stores":[{"store_id":"900173116","state":"ENABLE"},{"store_id":"900173117","state":"ENABLE"}]}]""");
            return Json("""{"message":"OK"}""");
        }

        private static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
    }

    private sealed record CapturedRequest(
        string Path,
        string Query,
        string Body,
        string? Authorization);
}
