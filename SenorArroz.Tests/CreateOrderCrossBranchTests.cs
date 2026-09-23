using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class CreateOrderCrossBranchTests
{
    [Theory]
    [InlineData(Roles.Cashier, 1)]
    [InlineData(Roles.Cashier, 2)]
    [InlineData(Roles.Admin, 2)]
    public async Task Authorized_roles_can_create_for_active_branch_in_same_tenant(string role, int destinationBranchId)
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db);
        var (handler, getCreatedOrder) = BuildHandler(db, role, assignedBranchId: 1);

        await handler.Handle(new CreateOrderCommand { Order = OnsiteOrder(destinationBranchId) }, default);

        var order = Assert.IsType<Order>(getCreatedOrder());
        Assert.Equal(destinationBranchId, order.BranchId);
        Assert.Equal(99, order.TakenById);
        Assert.Equal(1, order.TenantId);
    }

    [Fact]
    public async Task Nonexistent_destination_branch_is_rejected()
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db);
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new CreateOrderCommand { Order = OnsiteOrder(999) }, default));

        Assert.Contains("no existe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inactive_destination_branch_is_rejected()
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db);
        var branch = await db.Branches.SingleAsync(x => x.Id == 2);
        branch.IsActive = false;
        await db.SaveChangesAsync();
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new CreateOrderCommand { Order = OnsiteOrder(2) }, default));

        Assert.Contains("no está activa", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Destination_branch_from_another_tenant_is_rejected()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var tenantTwoDb = CreateDb(databaseName, 2))
        {
            tenantTwoDb.Branches.Add(new Branch { Id = 20, TenantId = 2, Name = "Otro tenant", IsActive = true });
            await tenantTwoDb.SaveChangesAsync();
        }

        await using var db = CreateDb(databaseName, 1);
        await SeedTenantOneAsync(db);
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new CreateOrderCommand { Order = OnsiteOrder(20) }, default));
    }

    [Fact]
    public async Task Customer_from_another_tenant_is_rejected()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var tenantTwoDb = CreateDb(databaseName, 2))
        {
            tenantTwoDb.Branches.Add(new Branch { Id = 20, TenantId = 2, Name = "Otro tenant", IsActive = true });
            tenantTwoDb.Customers.Add(new Customer { Id = 20, TenantId = 2, BranchId = 20, Name = "Ajeno", Active = true });
            await tenantTwoDb.SaveChangesAsync();
        }

        await using var db = CreateDb(databaseName, 1);
        await SeedTenantOneAsync(db);
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);
        var dto = OnsiteOrder(1);
        dto.CustomerId = 20;

        await Assert.ThrowsAsync<BusinessException>(() =>
            handler.Handle(new CreateOrderCommand { Order = dto }, default));
    }

    [Fact]
    public async Task Address_from_another_customer_is_rejected()
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db);
        db.Customers.Add(new Customer { Id = 2, BranchId = 1, Name = "Otro", Active = true });
        db.Addresses.Add(new Address { Id = 2, CustomerId = 2, AddressText = "Otra dirección" });
        await db.SaveChangesAsync();
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateOrderCommand { Order = DeliveryOrder(1, customerId: 1, addressId: 2) }, default));
    }

    [Fact]
    public async Task Address_from_another_tenant_is_rejected()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var tenantTwoDb = CreateDb(databaseName, 2))
        {
            tenantTwoDb.Branches.Add(new Branch { Id = 20, TenantId = 2, Name = "Otro tenant", IsActive = true });
            tenantTwoDb.Customers.Add(new Customer { Id = 20, TenantId = 2, BranchId = 20, Name = "Ajeno", Active = true });
            tenantTwoDb.Addresses.Add(new Address { Id = 20, TenantId = 2, CustomerId = 20, AddressText = "Ajena" });
            await tenantTwoDb.SaveChangesAsync();
        }

        await using var db = CreateDb(databaseName, 1);
        await SeedTenantOneAsync(db);
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateOrderCommand { Order = DeliveryOrder(1, customerId: 1, addressId: 20) }, default));
    }

    [Fact]
    public async Task Covered_address_uses_backend_fee_for_destination_branch()
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db, covered: true, deliveryFee: 4200);
        var (handler, getCreatedOrder) = BuildHandler(db, Roles.Cashier, 1);
        var dto = DeliveryOrder(2);
        dto.DeliveryFee = 1;

        await handler.Handle(new CreateOrderCommand { Order = dto }, default);

        Assert.Equal(4200, getCreatedOrder()!.DeliveryFee);
    }

    [Fact]
    public async Task Uncovered_address_is_rejected()
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db, covered: false, deliveryFee: 4200);
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateOrderCommand { Order = DeliveryOrder(2) }, default));

        Assert.Contains("fuera de cobertura", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_address_branch_is_rejected_instead_of_trusting_frontend_fee()
    {
        await using var db = CreateDb();
        await SeedTenantOneAsync(db, includeAddressService: false);
        var (handler, _) = BuildHandler(db, Roles.Cashier, 1);

        var exception = await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(
            new CreateOrderCommand { Order = DeliveryOrder(2) }, default));

        Assert.Contains("no tiene servicio", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateDb(string? name = null, int tenantId = 1)
    {
        var tenantContext = new TestTenantContext(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
                .Options,
            currentTenant: tenantContext,
            tenantExecutionContext: tenantContext);
    }

    private static async Task SeedTenantOneAsync(
        ApplicationDbContext db,
        bool covered = true,
        int deliveryFee = 5000,
        bool includeAddressService = true)
    {
        db.Branches.AddRange(
            new Branch { Id = 1, Name = "Santander", IsActive = true },
            new Branch { Id = 2, Name = "Manrique", IsActive = true });
        db.Customers.Add(new Customer { Id = 1, BranchId = 1, Name = "Cliente", Active = true });
        db.Addresses.Add(new Address { Id = 1, CustomerId = 1, AddressText = "Calle 1" });
        if (includeAddressService)
        {
            db.AddressBranches.Add(new AddressBranch
            {
                Id = 1,
                AddressId = 1,
                BranchId = 2,
                DeliveryFee = deliveryFee,
                IsCovered = covered
            });
        }
        await db.SaveChangesAsync();
    }

    private static (CreateOrderHandler Handler, Func<Order?> CreatedOrder) BuildHandler(
        ApplicationDbContext db,
        string role,
        int assignedBranchId)
    {
        Order? createdOrder = null;
        var repository = new Mock<IOrderRepository>();
        repository.Setup(x => x.CreateAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order order, CancellationToken _) =>
            {
                order.Id = 100;
                order.Branch = new Branch { Id = order.BranchId, TenantId = 1, Name = $"Sucursal {order.BranchId}" };
                order.TakenBy = new User { Id = 99, TenantId = 1, BranchId = assignedBranchId, Name = "Cajero" };
                createdOrder = order;
                return order;
            });
        repository.Setup(x => x.GetByIdWithFullDetailsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => createdOrder);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(99);
        currentUser.SetupGet(x => x.Role).Returns(role);
        currentUser.SetupGet(x => x.BranchId).Returns(assignedBranchId);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);

        var mapper = new MapperConfiguration(
            config => config.AddMaps(typeof(CreateOrderCommand).Assembly),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();
        var handler = new CreateOrderHandler(
            repository.Object,
            db,
            mapper,
            currentUser.Object,
            Mock.Of<IOrderNotificationService>(),
            Mock.Of<IClock>(clock => clock.UtcNow == new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc)));

        return (handler, () => createdOrder);
    }

    private static CreateOrderDto OnsiteOrder(int branchId) => new()
    {
        BranchId = branchId,
        TakenById = 12345,
        Type = OrderType.Onsite,
        OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 10000 }]
    };

    private static CreateOrderDto DeliveryOrder(int branchId, int customerId = 1, int addressId = 1) => new()
    {
        BranchId = branchId,
        TakenById = 12345,
        CustomerId = customerId,
        AddressId = addressId,
        GuestName = "Cliente",
        Type = OrderType.Delivery,
        DeliveryFee = 99999,
        OrderDetails = [new CreateOrderDetailDto { ProductId = 1, Quantity = 1, UnitPrice = 10000 }]
    };
}
