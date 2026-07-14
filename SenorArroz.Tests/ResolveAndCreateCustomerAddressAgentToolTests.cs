using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class ResolveAndCreateCustomerAddressAgentToolTests
{
    [Fact]
    public async Task ExactAddress_CreatesWithBackendDataAndSelectsIt()
    {
        await using var fixture = await Fixture.Create(address => Exact(address, 6.251m, -75.581m, "Santander"));

        var result = await fixture.Execute();

        Assert.True(result.Success);
        var saved = Assert.Single(await fixture.Db.Addresses.ToListAsync());
        Assert.Equal(10, saved.CustomerId);
        Assert.Equal(100, saved.NeighborhoodId);
        Assert.Equal("Carrera 65 # 95-24", saved.AddressText);
        Assert.Equal("Carrera 65 # 95-24", saved.OriginalAddressText);
        Assert.Equal("Casa de puerta negra", saved.AdditionalInfo);
        Assert.Equal(6.251m, saved.Latitude);
        Assert.Equal(-75.581m, saved.Longitude);
        Assert.Equal(7000, saved.DeliveryFee);
        Assert.True(saved.IsPrimary);
        var state = await fixture.State.LoadAsync(1);
        Assert.Equal(saved.Id, state.SelectedAddressId);
        Assert.Equal(OrderType.Delivery, state.OrderType);
    }

    [Fact]
    public async Task EquivalentExistingAddress_IsReusedAndSelected()
    {
        await using var fixture = await Fixture.Create(address => Exact(address, 6.251m, -75.581m, "Santander"));
        fixture.Db.Addresses.Add(new Address
        {
            Id = 200,
            CustomerId = 10,
            NeighborhoodId = 100,
            AddressText = "Cra 65 #95-24",
            DeliveryFee = 7000
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute();

        Assert.True(result.Success);
        Assert.Equal("customer_address_reused_and_selected", result.Code);
        Assert.Single(await fixture.Db.Addresses.ToListAsync());
        Assert.Equal(200, (await fixture.State.LoadAsync(1)).SelectedAddressId);
    }

    [Fact]
    public async Task InexactOriginal_UsesSingleExactNeighborButStoresOriginalText()
    {
        await using var fixture = await Fixture.Create(address => address.EndsWith("23", StringComparison.Ordinal)
            ? Exact("Carrera 65 # 95-23, Medellín", 6.25m, -75.58m, "Santander")
            : address.EndsWith("25", StringComparison.Ordinal)
                ? NoResults()
                : Inexact(address, "RANGE_INTERPOLATED"));

        var result = await fixture.Execute();

        Assert.True(result.Success);
        var saved = Assert.Single(await fixture.Db.Addresses.ToListAsync());
        Assert.Equal("Carrera 65 # 95-24", saved.AddressText);
        Assert.Equal(6.25m, saved.Latitude);
    }

    [Fact]
    public async Task DifferentExactNeighbors_TransfersToHumanAndDoesNotCreate()
    {
        await using var fixture = await Fixture.Create(address => address.EndsWith("23", StringComparison.Ordinal)
            ? Exact("Carrera 65 # 95-23, Medellín", 6.25m, -75.58m, "Santander")
            : address.EndsWith("25", StringComparison.Ordinal)
                ? Exact("Carrera 65 # 95-25, Medellín", 6.26m, -75.59m, "Santander")
                : Inexact(address, "APPROXIMATE"));

        var result = await fixture.Execute();

        Assert.True(result.TransferredToHuman);
        Assert.Empty(await fixture.Db.Addresses.ToListAsync());
        Assert.Equal(WhatsAppAttentionMode.WaitingForHuman, (await fixture.Db.WhatsAppConversations.FindAsync(1))!.AttentionMode);
    }

    [Fact]
    public async Task UnknownNeighborhood_UsesGoogleNeighborhoodAndValidatesCurrentBranch()
    {
        await using var fixture = await Fixture.Create(address => Exact(address, 6.251m, -75.581m, "Santander"));

        var result = await fixture.Execute(neighborhood: "", doesNotKnowNeighborhood: true);

        Assert.True(result.Success);
        Assert.Equal(100, (await fixture.Db.Addresses.SingleAsync()).NeighborhoodId);
    }

    [Fact]
    public async Task NeighborhoodFromAnotherBranch_IsNeverAccepted()
    {
        await using var fixture = await Fixture.Create(address => Exact(address, 6.251m, -75.581m, "Fuera"));
        fixture.Db.Neighborhoods.Add(new Neighborhood { Id = 101, BranchId = 2, Name = "Fuera", Active = true, DeliveryFee = 1 });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute(neighborhood: "", doesNotKnowNeighborhood: true);

        Assert.True(result.TransferredToHuman);
        Assert.Empty(await fixture.Db.Addresses.ToListAsync());
    }

    [Fact]
    public async Task MissingGoogleConfiguration_TransfersToHuman()
    {
        await using var fixture = await Fixture.Create(_ => Exact("x", 1, 1, "Santander"), configured: false);

        var result = await fixture.Execute();

        Assert.True(result.TransferredToHuman);
        Assert.Empty(await fixture.Db.Addresses.ToListAsync());
    }

    [Fact]
    public async Task InactiveCustomer_TransfersWithoutCreating()
    {
        await using var fixture = await Fixture.Create(address => Exact(address, 1, 1, "Santander"));
        (await fixture.Db.Customers.FindAsync(10))!.Active = false;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute();

        Assert.True(result.TransferredToHuman);
        Assert.Empty(await fixture.Db.Addresses.ToListAsync());
    }

    [Theory]
    [InlineData(true, "street_address", "ROOFTOP")]
    [InlineData(false, "route", "ROOFTOP")]
    [InlineData(false, "street_address", "RANGE_INTERPOLATED")]
    [InlineData(false, "street_address", "GEOMETRIC_CENTER")]
    [InlineData(false, "street_address", "APPROXIMATE")]
    public async Task GoogleGeocoder_RejectsNonExactResults(bool partial, string resultType, string locationType)
    {
        var geocoder = new GoogleAddressGeocoder(
            new HttpClient(new StubHandler(address => GeocodeResponse(address, 1, 1, "Santander", partial, resultType, locationType))),
            Options.Create(new GoogleMapsRouteOptions { GeocodingApiKey = "key" }));

        var result = await geocoder.Resolve("Carrera 65 # 95-24", null, null, default);

        Assert.NotNull(result.Result);
        Assert.True(result.Result.RequiresConfirmation);
        Assert.NotEqual("exact", result.Result.Quality);
    }

    [Theory]
    [InlineData("REQUEST_DENIED", "credencial")]
    [InlineData("OVER_QUERY_LIMIT", "cuota")]
    [InlineData("UNKNOWN_ERROR", "temporalmente")]
    public async Task GoogleGeocoder_ExplainsGoogleServiceFailures(string status, string expectedError)
    {
        var geocoder = new GoogleAddressGeocoder(
            new HttpClient(new StubHandler(_ => JsonSerializer.Serialize(new { results = Array.Empty<object>(), status }))),
            Options.Create(new GoogleMapsRouteOptions { GeocodingApiKey = "key" }));

        var result = await geocoder.Resolve("Carrera 65 # 95-24", null, null, default);

        Assert.Null(result.Result);
        Assert.Contains(expectedError, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolSchema_ExposesOnlyCustomerProvidedFields()
    {
        await using var fixture = await Fixture.Create(address => Exact(address, 1, 1, "Santander"));
        var properties = fixture.Tool.ParametersSchema.GetProperty("properties")
            .EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToList();

        Assert.Equal([
            "additionalInformation",
            "address",
            "customerDoesNotKnowNeighborhood",
            "neighborhood"
        ], properties);
        Assert.DoesNotContain(properties, x => x.EndsWith("Id", StringComparison.Ordinal));
        new SenorArroz.Application.Common.Services.AiToolSchemaValidator().ValidateOrThrow([
            new(fixture.Tool.Name, fixture.Tool.Description, fixture.Tool.ParametersSchema)
        ]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(ApplicationDbContext db, WhatsAppSimpleOrderStateService state, ResolveAndCreateCustomerAddressAgentTool tool)
        {
            Db = db;
            State = state;
            Tool = tool;
        }

        public ApplicationDbContext Db { get; }
        public WhatsAppSimpleOrderStateService State { get; }
        public ResolveAndCreateCustomerAddressAgentTool Tool { get; }

        public static async Task<Fixture> Create(Func<string, string> response, bool configured = true)
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Branches.AddRange(new Branch { Id = 1, Name = "Centro" }, new Branch { Id = 2, Name = "Norte" });
            db.Neighborhoods.Add(new Neighborhood { Id = 100, BranchId = 1, Name = "Santander", Active = true, DeliveryFee = 7000 });
            db.Customers.Add(new Customer { Id = 10, BranchId = 1, Name = "María", Phone1 = "300", Active = true });
            db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 1, BranchId = 1, CustomerId = 10, PhoneNumber = "57300", AttentionMode = WhatsAppAttentionMode.Ai });
            db.WhatsAppMessages.Add(new WhatsAppMessage { Id = 50, ConversationId = 1, Direction = WhatsAppMessageDirection.Inbound, Type = WhatsAppMessageType.Text, TextBody = "dirección", Status = WhatsAppMessageStatus.Received, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var clock = Mock.Of<IClock>(x => x.UtcNow == new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc));
            var state = new WhatsAppSimpleOrderStateService(db, clock);
            var geocoder = new GoogleAddressGeocoder(
                new HttpClient(new StubHandler(response)),
                Options.Create(new GoogleMapsRouteOptions { GeocodingApiKey = configured ? "key" : null }));
            var sender = new Mock<IWhatsAppAutomaticMessageSender>();
            sender.Setup(x => x.SendTransferTextAsync(1, 50, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WhatsAppAutomaticSendResult(true, false, "wamid", null));
            var human = new RequestHumanAssistanceAgentTool(
                db,
                new WhatsAppAttentionService(),
                Mock.Of<IWhatsAppNotificationService>(),
                sender.Object,
                clock,
                NullLogger<RequestHumanAssistanceAgentTool>.Instance);
            var tool = new ResolveAndCreateCustomerAddressAgentTool(
                db,
                new CustomerAddressResolutionService(db, new RegisteredNeighborhoodResolver(db), geocoder, clock),
                state,
                human);
            return new(db, state, tool);
        }

        public async Task<AgentToolExecutionResult> Execute(string neighborhood = "Santander", bool doesNotKnowNeighborhood = false)
        {
            var arguments = JsonSerializer.SerializeToElement(new
            {
                address = "Carrera 65 # 95-24",
                neighborhood,
                additionalInformation = "Casa de puerta negra",
                customerDoesNotKnowNeighborhood = doesNotKnowNeighborhood
            });
            return await Tool.ExecuteAsync(new(1, 1, 50, CustomerId: 10, ExecutionId: "test"), arguments, default);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class StubHandler(Func<string, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            var encoded = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2))
                .First(x => x[0].TrimStart('?') == "address")[1];
            var address = Uri.UnescapeDataString(encoded);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(address), Encoding.UTF8, "application/json")
            });
        }
    }

    private static string NoResults() => """{"results":[],"status":"ZERO_RESULTS"}""";

    private static string Inexact(string address, string locationType) =>
        GeocodeResponse(address, 6.2m, -75.5m, "Santander", false, "street_address", locationType);

    private static string Exact(string address, decimal latitude, decimal longitude, string neighborhood) =>
        GeocodeResponse(address, latitude, longitude, neighborhood, false, "street_address", "ROOFTOP");

    private static string GeocodeResponse(
        string address,
        decimal latitude,
        decimal longitude,
        string neighborhood,
        bool partial,
        string resultType,
        string locationType) => JsonSerializer.Serialize(new
    {
        results = new[] { new
        {
            formatted_address = address,
            partial_match = partial,
            types = new[] { resultType },
            address_components = Components(neighborhood),
            geometry = new { location = new { lat = latitude, lng = longitude }, location_type = locationType }
        } },
        status = "OK"
    });

    private static object[] Components(string neighborhood) =>
    [
        new { long_name = "Carrera 65", types = new[] { "route" } },
        new { long_name = "95-24", types = new[] { "street_number" } },
        new { long_name = neighborhood, types = new[] { "neighborhood" } }
    ];
}
