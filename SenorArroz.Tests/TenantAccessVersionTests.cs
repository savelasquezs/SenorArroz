using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Application.Common.Helpers;

namespace SenorArroz.Tests;

public sealed class TenantAccessVersionTests
{
    [Fact]
    public void Storage_paths_are_disjoint_between_tenants()
    {
        Assert.Equal("tenants/1/menu/file.png", TenantStoragePath.Combine(new TestTenantContext(1), "menu/file.png"));
        Assert.Equal("tenants/2/menu/file.png", TenantStoragePath.Combine(new TestTenantContext(2), "menu/file.png"));
    }

    [Theory]
    [InlineData(TenantStatus.Active, true, 7, true)]
    [InlineData(TenantStatus.Suspended, true, 7, false)]
    [InlineData(TenantStatus.Cancelled, true, 7, false)]
    [InlineData(TenantStatus.Active, false, 7, false)]
    [InlineData(TenantStatus.Active, true, 8, false)]
    public async Task Access_requires_active_tenant_and_current_version(
        TenantStatus status,
        bool isActive,
        long tokenVersion,
        bool expected)
    {
        var tenantContext = new TestTenantContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, currentTenant: tenantContext, tenantExecutionContext: tenantContext);
        using (tenantContext.BeginSystemScope())
        {
            var tenant = new Tenant { Id = 1, Name = "Tenant", Slug = "tenant", Status = status, IsActive = isActive, AccessVersion = 7 };
            var branch = new Branch { Id = 1, TenantId = 1, Tenant = tenant, Name = "Branch", Address = "Address", Phone1 = "3000000000" };
            db.Tenants.Add(tenant);
            db.Branches.Add(branch);
            db.Users.Add(new User
            {
                Id = 10,
                TenantId = 1,
                Tenant = tenant,
                BranchId = 1,
                Branch = branch,
                Email = "admin@example.com",
                PasswordHash = "hash",
                Name = "Admin",
                Role = UserRole.Admin,
                Active = true
            });
            await db.SaveChangesAsync();
        }

        var repository = new AuthRepository(db, Mock.Of<IPasswordService>(), tenantContext);
        Assert.Equal(expected, await repository.IsTenantAccessCurrentAsync(10, 1, tokenVersion));
    }
}
