using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica que los repositorios modificados para usar EF.Functions.ILike siguen
/// devolviendo datos correctos cuando se invocan SIN el filtro de texto.
///
/// NOTA: EF.Functions.ILike es específico de PostgreSQL y lanza InvalidOperationException
/// con el proveedor InMemory. Los tests de la búsqueda case-insensitive requieren
/// pruebas de integración contra PostgreSQL real y quedan fuera de este plan.
/// </summary>
public class ILikeRegressionTests
{
    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static Branch MakeBranch(string name = "Test") => new()
    {
        Name = name,
        Address = "Calle 1",
        Phone1 = "3001111111",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CustomerRepository sin filtro de nombre → devuelve todos los clientes
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CustomerRepository_GetPaged_WithoutNameFilter_ReturnsAll()
    {
        using var ctx = CreateCtx(nameof(CustomerRepository_GetPaged_WithoutNameFilter_ReturnsAll));

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        ctx.Customers.AddRange(
            new Customer { Name = "Ana García",    Phone1 = "111", BranchId = branch.Id, Active = true, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Bruno López",   Phone1 = "222", BranchId = branch.Id, Active = true, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Carlos Ramírez", Phone1 = "333", BranchId = branch.Id, Active = true, CreatedAt = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        // Sin filtro de nombre → no invoca ILike, funciona en InMemory
        var result = await repo.GetPagedAsync(branchId: branch.Id, page: 1, pageSize: 10);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.Items.Count());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. BranchRepository sin filtros → devuelve todas las sucursales
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task BranchRepository_GetPaged_WithoutFilters_ReturnsAll()
    {
        using var ctx = CreateCtx(nameof(BranchRepository_GetPaged_WithoutFilters_ReturnsAll));

        ctx.Branches.AddRange(
            MakeBranch("Norte"),
            MakeBranch("Sur")
        );
        await ctx.SaveChangesAsync();

        var repo = new BranchRepository(ctx);
        // Sin filtros de name ni address → no invoca ILike, funciona en InMemory
        var result = await repo.GetPagedAsync(page: 1, pageSize: 10);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. SupplierRepository sin filtro de búsqueda → devuelve todos los proveedores
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task SupplierRepository_GetPaged_WithoutSearch_ReturnsAll()
    {
        using var ctx = CreateCtx(nameof(SupplierRepository_GetPaged_WithoutSearch_ReturnsAll));

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        ctx.Suppliers.AddRange(
            new Supplier { Name = "Proveedor A", Phone = "3001234567", BranchId = branch.Id, CreatedAt = DateTime.UtcNow },
            new Supplier { Name = "Proveedor B", Phone = "3007654321", BranchId = branch.Id, CreatedAt = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var repo = new SupplierRepository(ctx);
        // Sin filtro de búsqueda → no invoca ILike, funciona en InMemory
        var result = await repo.GetPagedAsync(
            branchId: branch.Id,
            search: null,
            page: 1,
            pageSize: 10,
            sortBy: "name",
            sortOrder: "asc");

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count());
    }
}
