using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class WompiOrderDeletionTests
{
    [Fact]
    public void App_payment_relationship_sets_attempt_reference_to_null_on_delete()
    {
        using var db = CreateDb();
        var foreignKey = db.Model.FindEntityType(typeof(WompiPaymentAttempt))!
            .GetForeignKeys()
            .Single(x => x.Properties.Single().Name == nameof(WompiPaymentAttempt.AppPaymentId));

        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Route_proposal_stop_is_deleted_with_order()
    {
        using var db = CreateDb();
        var foreignKey = db.Model.FindEntityType(typeof(DeliveryRouteProposalStop))!
            .GetForeignKeys()
            .Single(x => x.Properties.Single().Name == nameof(DeliveryRouteProposalStop.OrderId));

        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public async Task Delete_order_removes_wompi_attempts_before_order()
    {
        await using var db = CreateDb();
        var order = new Order
        {
            Id = 10,
            BranchId = 1,
            TakenById = 1,
            Type = OrderType.Delivery,
            Status = OrderStatus.Taken,
            Total = 22_000,
            OrderSource = "web",
        };
        db.Orders.Add(order);
        db.OrderDetails.Add(new OrderDetail
        {
            Id = 30,
            OrderId = order.Id,
            ProductId = 1,
            Quantity = 1,
            UnitPrice = 22_000,
            Subtotal = 22_000,
            Order = order,
        });
        db.WompiPaymentAttempts.Add(new WompiPaymentAttempt
        {
            Id = 20,
            TenantId = 1,
            OrderId = order.Id,
            IntegrationId = 1,
            Reference = "SA-TEST",
            PublicKeySnapshot = "pub_test",
            IntegritySignature = "signature",
            EncryptedEventsSecretSnapshot = "secret",
            ExpectedAmountInCents = 2_200_000,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            Order = order,
        });
        await db.SaveChangesAsync();

        var repository = new OrderRepository(db, Mock.Of<IClock>());
        await repository.DeleteAsync(order.Id);

        Assert.Empty(await db.WompiPaymentAttempts.ToListAsync());
        Assert.Empty(await db.OrderDetails.ToListAsync());
        Assert.Empty(await db.Orders.ToListAsync());
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
