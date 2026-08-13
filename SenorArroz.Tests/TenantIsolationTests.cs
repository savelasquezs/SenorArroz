using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class TenantIsolationTests
{
    [Fact]
    public async Task Queries_only_return_authenticated_tenant_rows()
    {
        var options = Options();
        await SeedAsync(options);

        await using var db = new ApplicationDbContext(options, currentTenant: new TenantContext(1));

        var branches = await db.Branches.AsNoTracking().ToListAsync();
        var customers = await db.Customers.AsNoTracking().ToListAsync();

        Assert.Single(branches);
        Assert.Equal(1, branches[0].TenantId);
        Assert.Single(customers);
        Assert.Equal("Cliente A", customers[0].Name);
        Assert.Null(await db.Customers.SingleOrDefaultAsync(x => x.Id == 2));
    }

    [Fact]
    public async Task Cross_tenant_mutation_is_rejected_even_with_valid_id()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = new ApplicationDbContext(options, currentTenant: new TenantContext(1));
        db.Customers.Update(new Customer { Id = 2, TenantId = 2, BranchId = 2, Name = "Intento cruzado" });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("otro tenant", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task New_operational_rows_receive_authenticated_tenant_id()
    {
        var options = Options();
        await SeedAsync(options);
        await using var db = new ApplicationDbContext(options, currentTenant: new TenantContext(1));
        var branch = new Branch { Name = "Nueva", Address = "DirecciÃ³n", Phone1 = "3000000000" };
        db.Branches.Add(branch);

        await db.SaveChangesAsync();

        Assert.Equal(1, branch.TenantId);
    }

    [Fact]
    public async Task Platform_and_operational_identity_can_share_email()
    {
        var options = Options();
        await using var db = new ApplicationDbContext(options);
        db.Tenants.Add(new Tenant { Id = 1, DisplayName = "SeÃ±or Arroz", Slug = "senor-arroz", Status = TenantStatus.Active });
        db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "Principal", Address = "DirecciÃ³n", Phone1 = "3000000000" });
        db.Users.Add(new User { Id = 1, TenantId = 1, BranchId = 1, Name = "Administrador", Email = "santyvano@outlook.com", Phone = "3000000000", PasswordHash = "hash", Role = UserRole.Superadmin });
        db.PlatformUsers.Add(new PlatformUser { Id = 1, Name = "Administrador SaaS", Email = "santyvano@outlook.com", PasswordHash = "hash" });

        await db.SaveChangesAsync();

        var operational = await db.Users.SingleAsync();
        var platform = await db.PlatformUsers.SingleAsync();
        Assert.Equal(operational.Email, platform.Email);
    }

    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

    private static async Task SeedAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var db = new ApplicationDbContext(options);
        db.Tenants.AddRange(
            new Tenant { Id = 1, DisplayName = "Tenant A", Slug = "tenant-a", Status = TenantStatus.Active },
            new Tenant { Id = 2, DisplayName = "Tenant B", Slug = "tenant-b", Status = TenantStatus.Active });
        db.Branches.AddRange(
            new Branch { Id = 1, TenantId = 1, Name = "Sucursal A", Address = "A", Phone1 = "1" },
            new Branch { Id = 2, TenantId = 2, Name = "Sucursal B", Address = "B", Phone1 = "2" });
        db.Customers.AddRange(
            new Customer { Id = 1, TenantId = 1, BranchId = 1, Name = "Cliente A" },
            new Customer { Id = 2, TenantId = 2, BranchId = 2, Name = "Cliente B" });
        await db.SaveChangesAsync();
    }

    private sealed class TenantContext(int tenantId) : ICurrentTenant
    {
        public int TenantId { get; } = tenantId;
        public Guid? TenantPublicId { get; } = Guid.NewGuid();
        public bool HasTenant => true;
        public bool CanAccessAllTenants => false;
    }
}
