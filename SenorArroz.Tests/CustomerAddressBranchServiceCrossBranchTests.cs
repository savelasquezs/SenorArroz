using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class CustomerAddressBranchServiceCrossBranchTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task CashierCanSaveServiceInAnyActiveTenantBranch(int destinationBranchId)
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var controller = CreateController(db);

        var result = await controller.Upsert(10, 20,
            new UpsertCustomerAddressBranchServiceDto(4500, 6.25m, -75.58m),
            destinationBranchId, true, default);

        Assert.IsType<OkObjectResult>(result.Result);
        var service = await db.AddressBranches.SingleAsync(x => x.AddressId == 20);
        Assert.Equal(destinationBranchId, service.BranchId);
        Assert.Equal(1, service.TenantId);
        Assert.Equal(4500, service.DeliveryFee);
        Assert.True(service.IsCovered);
        Assert.Equal(6.25m, (await db.Addresses.SingleAsync(x => x.Id == 20)).Latitude);
    }

    [Fact]
    public async Task ARemoteAddressEditWithoutOrderContextIsRejected()
    {
        await using var db = CreateDb();
        await SeedAsync(db);

        var result = await CreateController(db).Upsert(10, 20,
            new UpsertCustomerAddressBranchServiceDto(5000),
            3, false, default);

        Assert.IsType<ForbidResult>(result.Result);
        Assert.Empty(await db.AddressBranches.ToListAsync());
    }

    [Fact]
    public async Task InactiveBranchIsNotAvailableForOrderCoverage()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.Branches.Single(x => x.Id == 4).IsActive = false;
        await db.SaveChangesAsync();

        var result = await CreateController(db).Upsert(10, 20,
            new UpsertCustomerAddressBranchServiceDto(5000),
            4, true, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Empty(await db.AddressBranches.ToListAsync());
    }

    [Fact]
    public async Task ABranchFromAnotherTenantCannotReceiveAddressService()
    {
        var name = Guid.NewGuid().ToString();
        await using (var otherTenant = CreateDb(name, 2))
        {
            otherTenant.Branches.Add(new Branch { Id = 99, TenantId = 2, Name = "Otro restaurante", IsActive = true });
            await otherTenant.SaveChangesAsync();
        }
        await using var db = CreateDb(name);
        await SeedAsync(db);

        var result = await CreateController(db).Upsert(10, 20,
            new UpsertCustomerAddressBranchServiceDto(5000),
            99, true, default);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Empty(await db.AddressBranches.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LocalAddressServiceKeepsTheExistingEndpointBehavior(bool branchIsActive)
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        db.Branches.Single(x => x.Id == 1).IsActive = branchIsActive;
        await db.SaveChangesAsync();

        var result = await CreateController(db).Upsert(10, 20,
            new UpsertCustomerAddressBranchServiceDto(3500),
            null, false, default);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, (await db.AddressBranches.SingleAsync()).BranchId);
    }

    private static CustomerAddressBranchServiceController CreateController(ApplicationDbContext db)
    {
        var branchContext = new Mock<IBranchContext>();
        branchContext.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(1);
        return new CustomerAddressBranchServiceController(db, branchContext.Object);
    }

    private static ApplicationDbContext CreateDb(string? name = null, int tenantId = 1)
    {
        var tenant = new TestTenantContext(tenantId);
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options,
            currentTenant: tenant,
            tenantExecutionContext: tenant);
    }

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Branches.AddRange(
            new Branch { Id = 1, Name = "Origen", TenantId = 1, IsActive = true },
            new Branch { Id = 2, Name = "Destino A", TenantId = 1, IsActive = true },
            new Branch { Id = 3, Name = "Destino B", TenantId = 1, IsActive = true },
            new Branch { Id = 4, Name = "Destino C", TenantId = 1, IsActive = true });
        db.Customers.Add(new Customer { Id = 10, TenantId = 1, BranchId = 1, Name = "Cliente" });
        db.Addresses.Add(new Address { Id = 20, TenantId = 1, CustomerId = 10, AddressText = "Calle 10" });
        await db.SaveChangesAsync();
    }
}
