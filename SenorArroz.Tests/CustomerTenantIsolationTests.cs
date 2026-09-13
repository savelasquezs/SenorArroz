using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class CustomerTenantIsolationTests
{
    [Fact]
    public async Task PhoneIdentity_IsGlobalAcrossBranches_ButSeparatedBetweenTenants()
    {
        await using var db = CreateDb();
        var tenantOneBranchA = new Branch { Id = 1, TenantId = 1, Name = "A", Address = "A", Phone1 = "1" };
        var tenantOneBranchB = new Branch { Id = 2, TenantId = 1, Name = "B", Address = "B", Phone1 = "2" };
        var tenantTwoBranch = new Branch { Id = 3, TenantId = 2, Name = "C", Address = "C", Phone1 = "3" };
        db.Branches.AddRange(tenantOneBranchA, tenantOneBranchB, tenantTwoBranch);
        db.Customers.AddRange(
            Customer(10, 1, tenantOneBranchA, "Tenant one", "3001234567"),
            Customer(20, 2, tenantTwoBranch, "Tenant two", "3001234567"));
        await db.SaveChangesAsync();

        var tenantOne = new CustomerRepository(db, new FixedTenant(1));
        var tenantTwo = new CustomerRepository(db, new FixedTenant(2));

        Assert.Equal(10, (await tenantOne.GetByPhoneAsync("+57 300 123 4567", tenantOneBranchB.TenantId))!.Id);
        Assert.Equal(20, (await tenantTwo.GetByPhoneAsync("3001234567", tenantTwoBranch.TenantId))!.Id);
        Assert.Null(await tenantOne.GetByIdAsync(20));
    }

    [Fact]
    public async Task ExactMobileSearch_IsGlobalWithinTenant_RegardlessOfOriginBranch()
    {
        await using var db = CreateDb();
        var originBranch = new Branch { Id = 1, TenantId = 1, Name = "Origin", Address = "A", Phone1 = "1" };
        var operatingBranch = new Branch { Id = 2, TenantId = 1, Name = "Operating", Address = "B", Phone1 = "2" };
        db.Branches.AddRange(originBranch, operatingBranch);
        db.Customers.Add(Customer(10, 1, originBranch, "Global mobile", "3022074761"));
        await db.SaveChangesAsync();

        var repository = new CustomerRepository(db, new FixedTenant(1));

        var result = await repository.GetPagedAsync(
            branchId: operatingBranch.Id,
            search: "3022074761",
            active: true,
            page: 1,
            pageSize: 10);

        Assert.Single(result.Items);
        Assert.Equal(10, result.Items.Single().Id);
    }

    [Fact]
    public async Task AddressRepository_DoesNotExposeAnotherTenant()
    {
        await using var db = CreateDb();
        var branchOne = new Branch { Id = 1, TenantId = 1, Name = "A", Address = "A", Phone1 = "1" };
        var branchTwo = new Branch { Id = 2, TenantId = 2, Name = "B", Address = "B", Phone1 = "2" };
        var customerOne = Customer(10, 1, branchOne, "One", "3001111111");
        var customerTwo = Customer(20, 2, branchTwo, "Two", "3002222222");
        db.AddRange(branchOne, branchTwo, customerOne, customerTwo,
            new Address { Id = 100, TenantId = 1, CustomerId = 10, AddressText = "Calle 1", DeliveryFee = 1000 },
            new Address { Id = 200, TenantId = 2, CustomerId = 20, AddressText = "Calle 2", DeliveryFee = 2000 });
        await db.SaveChangesAsync();

        var repository = new AddressRepository(db, new FixedTenant(1));

        Assert.NotNull(await repository.GetByIdAsync(100));
        Assert.Null(await repository.GetByIdAsync(200));
        Assert.False(await repository.DeleteAsync(200));
    }

    private static Customer Customer(int id, int tenantId, Branch branch, string name, string phone)
    {
        var customer = new Customer
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branch.Id,
            Branch = branch,
            Name = name,
            Phone1 = phone,
            Active = true
        };
        customer.Phones.Add(new CustomerPhone
        {
            TenantId = tenantId,
            PhoneNormalized = phone,
            IsPrimary = true,
            Active = true
        });
        return customer;
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed record FixedTenant(int TenantId) : ICurrentTenant
    {
        public bool HasTenant => true;
    }
}
