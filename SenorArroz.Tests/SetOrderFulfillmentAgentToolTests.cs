using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class SetOrderFulfillmentAgentToolTests
{
    [Fact]
    public async Task ConfirmedSavedAddress_SelectsDeliveryAndRecordsActivity()
    {
        await using var db = await Db();
        var state = State(db);
        var tool = new SetOrderFulfillmentAgentTool(db, state);
        using var arguments = JsonDocument.Parse("""{"orderType":"delivery","addressId":20}""");

        var result = await tool.ExecuteAsync(new(1, 1, CustomerId: 10), arguments.RootElement, default);

        Assert.True(result.Success);
        var saved = await state.LoadAsync(1);
        Assert.Equal(OrderType.Delivery, saved.OrderType);
        Assert.Equal(20, saved.SelectedAddressId);
        Assert.Contains(saved.Activities, x => x.Message.Contains("confirmó"));
    }

    [Fact]
    public async Task RejectedAddress_LeavesDeliveryPending()
    {
        await using var db = await Db();
        var state = State(db);
        await state.SaveAsync(1, new() { OrderType = OrderType.Delivery, SelectedAddressId = 20 });
        var tool = new SetOrderFulfillmentAgentTool(db, state);
        using var arguments = JsonDocument.Parse("""{"orderType":"delivery"}""");

        var result = await tool.ExecuteAsync(new(1, 1, CustomerId: 10), arguments.RootElement, default);

        Assert.True(result.Success);
        var saved = await state.LoadAsync(1);
        Assert.Equal(OrderType.Delivery, saved.OrderType);
        Assert.Null(saved.SelectedAddressId);
        Assert.Contains(saved.Activities, x => x.Message.Contains("rechazó"));
    }

    [Fact]
    public async Task AddressFromAnotherCustomer_IsRejected()
    {
        await using var db = await Db();
        var tool = new SetOrderFulfillmentAgentTool(db, State(db));
        using var arguments = JsonDocument.Parse("""{"orderType":"delivery","addressId":21}""");

        var result = await tool.ExecuteAsync(new(1, 1, CustomerId: 10), arguments.RootElement, default);

        Assert.False(result.Success);
        Assert.Equal("address_not_found", result.Code);
    }

    private static WhatsAppSimpleOrderStateService State(ApplicationDbContext db) =>
        new(db, Mock.Of<IClock>(x => x.UtcNow == DateTime.UtcNow));

    private static async Task<ApplicationDbContext> Db()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Branches.Add(new Branch { Id = 1, Name = "Centro" });
        db.Neighborhoods.Add(new Neighborhood { Id = 1, BranchId = 1, Name = "Santander", Active = true });
        db.Customers.AddRange(
            new Customer { Id = 10, BranchId = 1, Name = "María", Phone1 = "3001234567" },
            new Customer { Id = 11, BranchId = 1, Name = "Otro", Phone1 = "3001234568" });
        db.Addresses.AddRange(
            new Address { Id = 20, CustomerId = 10, NeighborhoodId = 1, AddressText = "Calle 1" },
            new Address { Id = 21, CustomerId = 11, NeighborhoodId = 1, AddressText = "Calle 2" });
        db.WhatsAppConversations.Add(new WhatsAppConversation { Id = 1, BranchId = 1, CustomerId = 10, PhoneNumber = "573001234567", AttentionMode = WhatsAppAttentionMode.Ai });
        await db.SaveChangesAsync();
        return db;
    }
}
