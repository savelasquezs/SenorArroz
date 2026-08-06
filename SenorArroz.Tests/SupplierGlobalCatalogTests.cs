using AutoMapper;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.Commands;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Application.Features.Suppliers.Queries;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public class SupplierGlobalCatalogTests
{
    private sealed class TestCurrentUser(string role = Roles.Cashier) : ICurrentUser
    {
        public int Id => 1;
        public string Role => role;
        public int BranchId => 1;
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext CreateCtx(string dbName)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        return new ApplicationDbContext(opts);
    }

    private static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddProfile<SupplierMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance).CreateMapper();

    private static Branch MakeBranch(int id, string name) => new()
    {
        Id = id,
        Name = name,
        Address = "Calle 1",
        Phone1 = $"300000000{id}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Repository_paged_returns_suppliers_from_all_origin_branches_and_null_origin()
    {
        using var db = CreateCtx(nameof(Repository_paged_returns_suppliers_from_all_origin_branches_and_null_origin));
        var santander = MakeBranch(1, "Santander");
        var manrique = MakeBranch(2, "Manrique");
        db.Branches.AddRange(santander, manrique);
        db.Suppliers.AddRange(
            new Supplier { Id = 1, BranchId = santander.Id, Name = "Proveedor Santander", Phone = "3000000001" },
            new Supplier { Id = 2, BranchId = manrique.Id, Name = "Proveedor Manrique", Phone = "3000000002" },
            new Supplier { Id = 3, BranchId = null, Name = "Proveedor General", Phone = "3000000003" });
        await db.SaveChangesAsync();

        var result = await new SupplierRepository(db).GetPagedAsync(
            search: null,
            page: 1,
            pageSize: 10,
            sortBy: "name",
            sortOrder: "asc");

        Assert.Equal(3, result.TotalCount);
        Assert.Contains(result.Items, s => s.BranchId == santander.Id);
        Assert.Contains(result.Items, s => s.BranchId == manrique.Id);
        Assert.Contains(result.Items, s => s.BranchId == null);
    }

    [Fact]
    public async Task By_branch_compat_query_returns_global_catalog()
    {
        using var db = CreateCtx(nameof(By_branch_compat_query_returns_global_catalog));
        var santander = MakeBranch(1, "Santander");
        var manrique = MakeBranch(2, "Manrique");
        db.Branches.AddRange(santander, manrique);
        db.Suppliers.AddRange(
            new Supplier { Id = 1, BranchId = santander.Id, Name = "Arroz Santander", Phone = "3000000001" },
            new Supplier { Id = 2, BranchId = manrique.Id, Name = "Arroz Manrique", Phone = "3000000002" });
        await db.SaveChangesAsync();

        var handler = new GetSuppliersByBranchHandler(new SupplierRepository(db), CreateMapper());
        var result = await handler.Handle(new GetSuppliersByBranchQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.BranchId == santander.Id);
        Assert.Contains(result, s => s.BranchId == manrique.Id);
    }

    [Fact]
    public async Task Create_supplier_does_not_require_branch_and_allows_duplicates()
    {
        using var db = CreateCtx(nameof(Create_supplier_does_not_require_branch_and_allows_duplicates));
        db.Suppliers.Add(new Supplier
        {
            Id = 1,
            BranchId = 1,
            Name = "Proveedor repetido",
            Phone = "3000000001"
        });
        await db.SaveChangesAsync();

        var handler = new CreateSupplierHandler(
            new SupplierRepository(db),
            CreateMapper(),
            new TestCurrentUser(Roles.Cashier));

        var created = await handler.Handle(new CreateSupplierCommand
        {
            Supplier = new CreateSupplierDto
            {
                Name = "Proveedor repetido",
                Phone = "3000000001"
            }
        }, CancellationToken.None);

        Assert.NotEqual(1, created.Id);
        Assert.Null(created.BranchId);
        Assert.Equal(2, await db.Suppliers.CountAsync());
    }
}
