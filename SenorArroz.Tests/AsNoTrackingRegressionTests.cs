using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

/// <summary>
/// Verifica que los métodos de lectura con AsNoTracking devuelvan datos correctos
/// y que las operaciones de escritura sigan funcionando (no se rompa nada).
/// </summary>
public class AsNoTrackingRegressionTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// <see cref="OrderRepository.UpdateAsync"/> usa transacción; InMemory no la soporta
    /// y por defecto lanza si no se ignora la advertencia.
    /// </summary>
    private static Branch MakeBranch(string name = "Sucursal Test") => new()
    {
        Name = name,
        Address = "Calle 1",
        Phone1 = "0000000",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. CustomerRepository.GetPagedAsync devuelve datos correctos con filtros
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CustomerRepository_Read_ReturnsCorrectData()
    {
        const string db = nameof(CustomerRepository_Read_ReturnsCorrectData);
        using var ctx = CreateContext(db);

        var branch1 = MakeBranch("Norte");
        var branch2 = MakeBranch("Sur");
        ctx.Branches.AddRange(branch1, branch2);
        await ctx.SaveChangesAsync();

        ctx.Customers.AddRange(
            new Customer { Name = "Ana García", Phone1 = "111", BranchId = branch1.Id, Active = true, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Bruno López", Phone1 = "222", BranchId = branch1.Id, Active = true, CreatedAt = DateTime.UtcNow },
            new Customer { Name = "Carlos Ruiz", Phone1 = "333", BranchId = branch2.Id, Active = true, CreatedAt = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var repo = new CustomerRepository(ctx);
        var result = await repo.GetPagedAsync(branchId: branch1.Id, page: 1, pageSize: 10);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, c => Assert.Equal(branch1.Id, c.BranchId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Regresión crítica: leer con AsNoTracking y luego actualizar con
    //    Update() explícito sigue persistiendo los cambios correctamente.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task CustomerRepository_UpdateAfterRead_StillPersists()
    {
        const string db = nameof(CustomerRepository_UpdateAfterRead_StillPersists);
        using var ctx = CreateContext(db);

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var customer = new Customer { Name = "Original", Phone1 = "555", BranchId = branch.Id, Active = true, CreatedAt = DateTime.UtcNow };
        ctx.Customers.Add(customer);
        await ctx.SaveChangesAsync();

        // Simula nueva request: limpiar el tracker (equivale a nuevo DbContext en producción)
        ctx.ChangeTracker.Clear();

        var repo = new CustomerRepository(ctx);
        var fetched = await repo.GetByIdAsync(customer.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Original", fetched.Name);

        // Modificar y guardar — el Update() explícito debe funcionar con entidad no rastreada
        fetched.Name = "Actualizado";
        await repo.UpdateAsync(fetched);

        // Limpiar tracker y verificar que el cambio persiste
        ctx.ChangeTracker.Clear();
        var updated = await repo.GetByIdAsync(customer.Id);
        Assert.Equal("Actualizado", updated!.Name);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. ProductRepository.GetByCategoryIdAsync devuelve productos correctos
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ProductRepository_Read_ReturnsCorrectData()
    {
        const string db = nameof(ProductRepository_Read_ReturnsCorrectData);
        using var ctx = CreateContext(db);

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var cat1 = new ProductCategory { Name = "Arroces", BranchId = branch.Id };
        var cat2 = new ProductCategory { Name = "Bebidas", BranchId = branch.Id };
        ctx.ProductCategories.AddRange(cat1, cat2);
        await ctx.SaveChangesAsync();

        ctx.Products.AddRange(
            new Product { Name = "Arroz Blanco", Price = 12000, CategoryId = cat1.Id, Active = true, Stock = 10, CreatedAt = DateTime.UtcNow },
            new Product { Name = "Arroz Integral", Price = 14000, CategoryId = cat1.Id, Active = true, Stock = 5, CreatedAt = DateTime.UtcNow },
            new Product { Name = "Agua", Price = 3000, CategoryId = cat2.Id, Active = true, Stock = 20, CreatedAt = DateTime.UtcNow }
        );
        await ctx.SaveChangesAsync();

        var repo = new ProductRepository(ctx);
        var products = await repo.GetByCategoryIdAsync(cat1.Id);

        Assert.Equal(2, products.Count());
        Assert.All(products, p => Assert.Equal(cat1.Id, p.CategoryId));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. UserRepository.GetByEmailAsync devuelve el usuario activo correcto
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task UserRepository_GetByEmail_ReturnsUser()
    {
        const string db = nameof(UserRepository_GetByEmail_ReturnsUser);
        using var ctx = CreateContext(db);

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        ctx.Users.Add(new User
        {
            Name = "Admin Test",
            Email = "admin@test.com",
            PasswordHash = "hash",
            Role = UserRole.Admin,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var repo = new UserRepository(ctx);
        var user = await repo.GetByEmailAsync("admin@test.com");

        Assert.NotNull(user);
        Assert.Equal("Admin Test", user.Name);
        Assert.Equal("admin@test.com", user.Email);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. OrderRepository.GetByIdAsync devuelve la orden correcta
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task OrderRepository_GetById_ReturnsOrder()
    {
        const string db = nameof(OrderRepository_GetById_ReturnsOrder);
        using var ctx = CreateContext(db);

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        // TakenById es requerido (int no nullable)
        var user = new User
        {
            Name = "Cajero",
            Email = "cajero@test.com",
            PasswordHash = "hash",
            Role = UserRole.Cashier,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var order = new Order
        {
            BranchId = branch.Id,
            TakenById = user.Id,
            Status = OrderStatus.Taken,
            Type = OrderType.Delivery,
            Total = 50000,
            Subtotal = 48000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();

        // Simula nueva request: limpiar tracker
        ctx.ChangeTracker.Clear();

        var repo = new OrderRepository(ctx, new SystemUtcClock());
        var fetched = await repo.GetByIdAsync(order.Id);

        Assert.NotNull(fetched);
        Assert.Equal(50000, fetched.Total);
        Assert.Equal(OrderStatus.Taken, fetched.Status);
    }

    [Fact]
    public async Task OrderRepository_Search_MapsAutomaticDeliveryIndicator()
    {
        await using var ctx = CreateContext(nameof(OrderRepository_Search_MapsAutomaticDeliveryIndicator));
        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var deliveryman = new User
        {
            Name = "Domiciliario",
            Email = "delivery-auto@test.com",
            Phone = "3000000001",
            PasswordHash = "hash",
            Role = UserRole.Deliveryman,
            BranchId = branch.Id,
            Active = true,
        };
        var cashier = new User
        {
            Name = "Cajero",
            Email = "cashier-auto@test.com",
            Phone = "3000000002",
            PasswordHash = "hash",
            Role = UserRole.Cashier,
            BranchId = branch.Id,
            Active = true,
        };
        ctx.Users.AddRange(deliveryman, cashier);
        await ctx.SaveChangesAsync();

        var route = new DeliveryRoute
        {
            BranchId = branch.Id,
            DeliverymanId = deliveryman.Id,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = DateTime.UtcNow,
        };
        ctx.DeliveryRoutes.Add(route);
        await ctx.SaveChangesAsync();

        var order = new Order
        {
            BranchId = branch.Id,
            TakenById = cashier.Id,
            DeliveryManId = deliveryman.Id,
            DeliveryRouteId = route.Id,
            Type = OrderType.Delivery,
            Status = OrderStatus.Delivered,
        };
        ctx.Orders.Add(order);
        await ctx.SaveChangesAsync();
        ctx.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            DeliveryRouteId = route.Id,
            OrderId = order.Id,
            StopSequence = 1,
            AutoDeliveredAtUtc = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var repository = new OrderRepository(ctx, new SystemUtcClock());
        var result = await repository.SearchOrdersAsync(
            branchId: branch.Id,
            page: 1,
            pageSize: 10);
        var mapper = new MapperConfiguration(
            config => config.AddProfile<OrderMappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();
        var dto = mapper.Map<OrderDto>(Assert.Single(result.Items));

        Assert.True(dto.WasAutomaticallyDelivered);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. GetAllByUserIdAsync (con AsNoTracking) NO rastrea entidades.
    //    Esto verifica la diferencia entre métodos de lectura puros y los
    //    métodos excluidos de AsNoTracking como GetByTokenAsync.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task RefreshToken_GetAllByUserId_WithAsNoTracking_DoesNotTrackEntities()
    {
        const string db = nameof(RefreshToken_GetAllByUserId_WithAsNoTracking_DoesNotTrackEntities);
        using var ctx = CreateContext(db);

        var branch = MakeBranch();
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();

        var user = new User
        {
            Name = "User Token",
            Email = "tokenuser@test.com",
            PasswordHash = "hash",
            Role = UserRole.Cashier,
            BranchId = branch.Id,
            Active = true,
            CreatedAt = DateTime.UtcNow
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        ctx.RefreshTokens.Add(new RefreshToken
        {
            Token = "mi-token-secreto",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        });
        await ctx.SaveChangesAsync();

        // Limpiar tracker para simular una nueva request
        ctx.ChangeTracker.Clear();

        var repo = new RefreshTokenRepository(ctx, new SystemUtcClock());

        // GetAllByUserIdAsync tiene AsNoTracking: devuelve datos pero no rastrea
        var tokens = await repo.GetAllByUserIdAsync(user.Id);
        Assert.Single(tokens);
        Assert.Empty(ctx.ChangeTracker.Entries<RefreshToken>());

        // GetByTokenAsync NO tiene AsNoTracking: rastrea la entidad
        ctx.ChangeTracker.Clear();
        var token = await repo.GetByTokenAsync("mi-token-secreto");
        Assert.NotNull(token);
        Assert.Single(ctx.ChangeTracker.Entries<RefreshToken>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. ExpenseRepository: GetById con Include y cambio de CategoryId no debe
    //    provocar conflicto de seguimiento de ExpenseCategory al actualizar.
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task ExpenseRepository_UpdateAfterGetWithCategory_ChangeCategoryId_Persists()
    {
        const string db = nameof(ExpenseRepository_UpdateAfterGetWithCategory_ChangeCategoryId_Persists);
        await using var ctx = CreateContext(db);

        var ec1 = new ExpenseCategory { Name = "Fijos", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var ec2 = new ExpenseCategory { Name = "Variables", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        ctx.ExpenseCategories.AddRange(ec1, ec2);
        await ctx.SaveChangesAsync();

        var expense = new Expense
        {
            Name = "Luz",
            CategoryId = ec1.Id,
            Unit = ExpenseUnit.Unit,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        ctx.Expenses.Add(expense);
        await ctx.SaveChangesAsync();

        ctx.ExpenseMenuTargets.Add(new ExpenseMenuTarget
        {
            ExpenseId = expense.Id,
            TargetType = ExpenseMenuTargetType.Product,
            TargetId = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();

        var repo = new ExpenseRepository(ctx);
        var fetched = await repo.GetByIdAsync(expense.Id);
        Assert.NotNull(fetched);
        Assert.Equal(ec1.Id, fetched.CategoryId);
        Assert.Equal(ec1.Id, fetched.Category.Id);
        Assert.NotEmpty(fetched.MenuTargets);

        fetched.Name = "Luz (edit)";
        fetched.CategoryId = ec2.Id;

        await repo.UpdateAsync(fetched);

        ctx.ChangeTracker.Clear();
        var after = await repo.GetByIdAsync(expense.Id);
        Assert.NotNull(after);
        Assert.Equal("Luz (edit)", after.Name);
        Assert.Equal(ec2.Id, after.CategoryId);
        Assert.Equal(ec2.Name, after.Category.Name);
    }
}
