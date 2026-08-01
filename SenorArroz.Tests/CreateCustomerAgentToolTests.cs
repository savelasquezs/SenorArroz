using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class CreateCustomerAgentToolTests
{
    [Fact]
    public async Task NameOnly_CreatesAndLinksCustomerForPickup()
    {
        await using var fixture = await Fixture.Create();

        var result = await fixture.Execute(new { name = "María Pérez" });

        Assert.True(result.Success);
        var customer = Assert.Single(await fixture.Db.Customers.ToListAsync());
        Assert.Equal("3001234567", customer.Phone1);
        Assert.Equal(1, customer.BranchId);
        var conversation = await fixture.Db.WhatsAppConversations.FindAsync(1);
        Assert.Equal(customer.Id, conversation!.CustomerId);
        Assert.Equal("María Pérez", conversation.ContactName);
        var state = await fixture.State.LoadAsync(1);
        Assert.Equal(OrderType.Onsite, state.OrderType);
        Assert.Null(state.SelectedAddressId);
    }

    [Fact]
    public async Task FullDelivery_CreatesCustomerAddressAndSelectionTogether()
    {
        await using var fixture = await Fixture.Create();

        var result = await fixture.Execute(DeliveryArguments());

        Assert.True(result.Success);
        var customer = Assert.Single(await fixture.Db.Customers.ToListAsync());
        var address = Assert.Single(await fixture.Db.Addresses.ToListAsync());
        Assert.Equal(customer.Id, address.CustomerId);
        Assert.Equal("Carrera 65 # 95-24", address.AddressText);
        Assert.Equal(100, address.NeighborhoodId);
        Assert.Equal(7000, address.DeliveryFee);
        Assert.True(address.IsPrimary);
        var state = await fixture.State.LoadAsync(1);
        Assert.Equal(OrderType.Delivery, state.OrderType);
        Assert.Equal(address.Id, state.SelectedAddressId);
    }

    [Fact]
    public async Task ExactNeighbor_KeepsOriginalCustomerAddressText()
    {
        await using var fixture = await Fixture.Create(address => address.EndsWith("23", StringComparison.Ordinal)
            ? Fixture.Exact(address, "Santander")
            : address.EndsWith("25", StringComparison.Ordinal)
                ? Fixture.NoResults()
                : Fixture.Inexact(address));

        var result = await fixture.Execute(DeliveryArguments());

        Assert.True(result.Success);
        Assert.Equal("Carrera 65 # 95-24", (await fixture.Db.Addresses.SingleAsync()).AddressText);
    }

    [Fact]
    public async Task ActiveCustomer_IsReusedWithoutOverwritingRegisteredName()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.Customers.Add(new Customer { Id = 20, BranchId = 1, Name = "Nombre registrado", Phone1 = "3001234567", Active = true });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute(new { name = "Nombre nuevo" });

        Assert.True(result.Success);
        var customer = Assert.Single(await fixture.Db.Customers.ToListAsync());
        Assert.Equal(20, customer.Id);
        Assert.Equal("Nombre registrado", customer.Name);
        Assert.Equal("Nombre registrado", (await fixture.Db.WhatsAppConversations.FindAsync(1))!.ContactName);
    }

    [Fact]
    public async Task InactiveCustomer_IsReactivatedAndRenamed()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.Customers.Add(new Customer { Id = 20, BranchId = 1, Name = "Anterior", Phone1 = "3001234567", Active = false });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute(new { name = "Nombre Actual" });

        Assert.True(result.Success);
        var customer = Assert.Single(await fixture.Db.Customers.ToListAsync());
        Assert.True(customer.Active);
        Assert.Equal("Nombre Actual", customer.Name);
    }

    [Fact]
    public async Task EquivalentAddress_IsReused()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.Customers.Add(new Customer { Id = 20, BranchId = 1, Name = "María", Phone1 = "3001234567", Active = true });
        fixture.Db.Addresses.Add(new Address { Id = 30, CustomerId = 20, NeighborhoodId = 100, AddressText = "Cra 65 #95-24", DeliveryFee = 7000 });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute(DeliveryArguments());

        Assert.True(result.Success);
        Assert.Single(await fixture.Db.Addresses.ToListAsync());
        Assert.Equal(30, (await fixture.State.LoadAsync(1)).SelectedAddressId);
    }

    [Fact]
    public async Task PartialDeliveryData_RequestsMissingDataWithoutWriting()
    {
        await using var fixture = await Fixture.Create();

        var result = await fixture.Execute(new { name = "María Pérez", address = "Carrera 65 # 95-24" });

        Assert.False(result.Success);
        Assert.True(result.RequiresUserInput);
        Assert.Equal("customer_address_data_required", result.Code);
        Assert.Empty(await fixture.Db.Customers.ToListAsync());
        Assert.Null((await fixture.Db.WhatsAppConversations.FindAsync(1))!.CustomerId);
    }

    [Fact]
    public async Task GoogleFailure_DoesNotCreateCustomerAndTransfers()
    {
        await using var fixture = await Fixture.Create(_ => Fixture.NoResults());

        var result = await fixture.Execute(DeliveryArguments());

        Assert.True(result.TransferredToHuman);
        Assert.Empty(await fixture.Db.Customers.ToListAsync());
        Assert.Empty(await fixture.Db.Addresses.ToListAsync());
    }

    [Fact]
    public async Task SamePhoneInAnotherBranch_IsNotReused()
    {
        await using var fixture = await Fixture.Create();
        fixture.Db.Customers.Add(new Customer { Id = 20, BranchId = 2, Name = "Otra sede", Phone1 = "3001234567", Active = true });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Execute(new { name = "Cliente Centro" });

        Assert.True(result.Success);
        var local = await fixture.Db.Customers.SingleAsync(x => x.BranchId == 1);
        Assert.Equal("Cliente Centro", local.Name);
        Assert.Equal(2, await fixture.Db.Customers.CountAsync());
    }

    [Fact]
    public async Task InvalidPhone_TransfersWithoutCreating()
    {
        await using var fixture = await Fixture.Create(phone: "123");

        var result = await fixture.Execute(new { name = "María Pérez" }, phone: "123");

        Assert.True(result.TransferredToHuman);
        Assert.Empty(await fixture.Db.Customers.ToListAsync());
    }

    [Fact]
    public async Task BsuidWithoutPhone_CreatesAndLinksCustomer()
    {
        await using var fixture = await Fixture.Create(phone: null, userId: "user.abc123", username: "@cliente_99");

        var result = await fixture.Execute(new { name = "Cliente WhatsApp" }, phone: null);

        Assert.True(result.Success);
        var customer = Assert.Single(await fixture.Db.Customers.ToListAsync());
        Assert.Null(customer.Phone1);
        Assert.Equal("user.abc123", customer.WhatsAppUserId);
        Assert.Equal("@cliente_99", customer.WhatsAppUsername);
        Assert.Equal(customer.Id, (await fixture.Db.WhatsAppConversations.FindAsync(1))!.CustomerId);
    }

    [Fact]
    public async Task ToolSchema_ExposesNoSecureOrBackendFields()
    {
        await using var fixture = await Fixture.Create();
        var properties = fixture.Tool.ParametersSchema.GetProperty("properties")
            .EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToList();

        Assert.Equal([
            "additionalInformation",
            "address",
            "customerDoesNotKnowNeighborhood",
            "name",
            "neighborhood"
        ], properties);
        Assert.DoesNotContain(properties, x => x is "phone" or "customerId" or "branchId" or "neighborhoodId" or "latitude" or "longitude" or "deliveryFee" or "orderType");
        new SenorArroz.Application.Common.Services.AiToolSchemaValidator().ValidateOrThrow([
            new(fixture.Tool.Name, fixture.Tool.Description, fixture.Tool.ParametersSchema)
        ]);
    }

    [Fact]
    public async Task Executor_RejectsPhoneAndBackendFieldsAsArguments()
    {
        await using var fixture = await Fixture.Create();
        var executor = new AgentToolExecutor([fixture.Tool], fixture.Db);

        var result = await executor.ExecuteAsync(
            fixture.Tool.Name,
            new(1, 1, 50),
            JsonSerializer.SerializeToElement(new { name = "María Pérez", phone = "3001234567" }),
            default);

        Assert.False(result.Success);
        Assert.Equal("invalid_arguments", result.Code);
        Assert.Contains("phone", result.Error);
        Assert.Empty(await fixture.Db.Customers.ToListAsync());
    }

    private static object DeliveryArguments() => new
    {
        name = "María Pérez",
        address = "Carrera 65 # 95-24",
        neighborhood = "Santander",
        additionalInformation = "Casa de puerta negra",
        customerDoesNotKnowNeighborhood = false
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(ApplicationDbContext db, WhatsAppSimpleOrderStateService state, CreateCustomerAgentTool tool)
        {
            Db = db;
            State = state;
            Tool = tool;
        }

        public ApplicationDbContext Db { get; }
        public WhatsAppSimpleOrderStateService State { get; }
        public CreateCustomerAgentTool Tool { get; }

        public static async Task<Fixture> Create(
            Func<string, string>? response = null,
            string? phone = "573001234567",
            string? userId = null,
            string? username = null)
        {
            response ??= address => Exact(address, "Santander");
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Branches.AddRange(new Branch { Id = 1, Name = "Centro" }, new Branch { Id = 2, Name = "Norte" });
            db.Neighborhoods.Add(new Neighborhood { Id = 100, BranchId = 1, Name = "Santander", Active = true, DeliveryFee = 7000 });
            db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 1, BranchId = 1, PhoneNumber = phone, WhatsAppUserId = userId, WhatsAppUsername = username, AttentionMode = WhatsAppAttentionMode.Ai });
            db.WhatsAppMessages.Add(new WhatsAppMessage { Id = 50, ConversationId = 1, Direction = WhatsAppMessageDirection.Inbound, Type = WhatsAppMessageType.Text, TextBody = "pedido", Status = WhatsAppMessageStatus.Received, Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var clock = Mock.Of<IClock>(x => x.UtcNow == new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc));
            var state = new WhatsAppSimpleOrderStateService(db, clock);
            var geocoder = new GoogleAddressGeocoder(
                new HttpClient(new StubHandler(response)),
                Options.Create(new GoogleMapsRouteOptions { GeocodingApiKey = "key" }));
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
            var resolver = new CustomerAddressResolutionService(db, new RegisteredNeighborhoodResolver(db), geocoder, clock);
            return new(db, state, new CreateCustomerAgentTool(db, resolver, state, human));
        }

        public Task<AgentToolExecutionResult> Execute(object arguments, string? phone = "573001234567") =>
            Tool.ExecuteAsync(
                new(1, 1, 50, PhoneNumber: phone, ExecutionId: "test"),
                JsonSerializer.SerializeToElement(arguments),
                default);

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        internal static string NoResults() => """{"results":[],"status":"ZERO_RESULTS"}""";
        internal static string Inexact(string address) => GeocodeResponse(address, "Santander", "APPROXIMATE");
        internal static string Exact(string address, string neighborhood) => GeocodeResponse(address, neighborhood, "ROOFTOP");

        private static string GeocodeResponse(string address, string neighborhood, string locationType) => JsonSerializer.Serialize(new
        {
            results = new[] { new
            {
                formatted_address = address,
                partial_match = false,
                types = new[] { "street_address" },
                address_components = new object[]
                {
                    new { long_name = "Carrera 65", types = new[] { "route" } },
                    new { long_name = "95-24", types = new[] { "street_number" } },
                    new { long_name = neighborhood, types = new[] { "neighborhood" } }
                },
                geometry = new { location = new { lat = 6.251m, lng = -75.581m }, location_type = locationType }
            } },
            status = "OK"
        });
    }

    private sealed class StubHandler(Func<string, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var encoded = request.RequestUri!.Query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2))
                .First(x => x[0].TrimStart('?') == "address")[1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response(Uri.UnescapeDataString(encoded)), Encoding.UTF8, "application/json")
            });
        }
    }
}
