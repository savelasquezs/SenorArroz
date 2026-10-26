using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace SenorArroz.Tests;

public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_POSTGRES_INTEGRATION_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Requiere RUN_POSTGRES_INTEGRATION_TESTS=true y Docker.";
        }
    }
}

public sealed class DeliveryAutoCompletionPostgreSqlConcurrencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<ApplicationDbContext> _options = null!;
    private readonly DateTime _now = new(2026, 8, 13, 18, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        await using var db = CreateDb();
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [PostgreSqlIntegrationFact]
    [Trait("Category", "PostgreSqlIntegration")]
    public async Task ConcurrentDeparturePoints_AttemptAutomaticDeliveryOnlyOnce()
    {
        var sender = new Mock<ISender>();
        var firstTransitionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTransition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transitionAttempts = 0;

        sender.Setup(x => x.Send(It.IsAny<ChangeOrderStatusCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ChangeOrderStatusCommand, CancellationToken>(async (command, cancellationToken) =>
            {
                Interlocked.Increment(ref transitionAttempts);
                firstTransitionEntered.TrySetResult();
                await releaseTransition.Task.WaitAsync(cancellationToken);

                await using var db = CreateDb();
                var order = await db.Orders.SingleAsync(x => x.Id == command.Id, cancellationToken);
                var stop = await db.DeliveryRouteStops.SingleAsync(x => x.OrderId == command.Id, cancellationToken);
                order.Status = OrderStatus.Delivered;
                stop.AutoDeliveredAtUtc = command.AutoDeliveredAtUtc;
                stop.AutoDeliveryTriggerLocationId = command.AutoDeliveryTriggerLocationId;
                stop.AutoDeliveryDepartureDistanceMeters = command.AutoDeliveryDepartureDistanceMeters;
                await db.SaveChangesAsync(cancellationToken);

                return new OrderDto
                {
                    Id = order.Id,
                    BranchId = order.BranchId,
                    Status = order.Status,
                    DeliveryManId = order.DeliveryManId,
                    DeliveryRouteId = order.DeliveryRouteId,
                };
            });

        await using var firstDb = CreateDb();
        await using var secondDb = CreateDb();
        var firstService = CreateService(firstDb, sender.Object);
        var secondService = CreateService(secondDb, sender.Object);
        var firstLocation = DepartureLocation(100);
        var secondLocation = DepartureLocation(101);

        var firstTask = firstService.EvaluateLocationAsync(firstLocation);
        await firstTransitionEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondTask = secondService.EvaluateLocationAsync(secondLocation);

        try
        {
            await Task.Delay(500);
            Assert.Equal(1, Volatile.Read(ref transitionAttempts));
            Assert.False(secondTask.IsCompleted);
        }
        finally
        {
            releaseTransition.TrySetResult();
        }

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(1, transitionAttempts);
        await using var verificationDb = CreateDb();
        var order = await verificationDb.Orders.AsNoTracking().SingleAsync(x => x.Id == 30);
        var stop = await verificationDb.DeliveryRouteStops.AsNoTracking().SingleAsync(x => x.OrderId == 30);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(firstLocation.Id, stop.AutoDeliveryTriggerLocationId);
    }

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentOfflineConfirmationsReuseOneDurableReceipt()
    {
        await using (var seed = CreateDb()) {
            seed.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = 77, DeliverymanId = 1, BranchId = 7,
                DeviceInstallationId = "fixture", StartedAt = _now.AddHours(-1), AutoCloseAt = _now.AddHours(2),
                Status = DeliveryWorkSessionStatus.Active, LastCommunicationAt = _now });
            await seed.SaveChangesAsync();
        }
        var attempts = 0;
        DeliveryOperationsService Operations(ApplicationDbContext db) {
            var sender = new Mock<ISender>();
            sender.Setup(x => x.Send(It.IsAny<ChangeOrderStatusCommand>(), It.IsAny<CancellationToken>()))
                .Returns<ChangeOrderStatusCommand, CancellationToken>(async (command, token) => {
                    Interlocked.Increment(ref attempts);
                    var order = await db.Orders.SingleAsync(o => o.Id == command.Id, token);
                    order.Status = OrderStatus.Delivered;
                    await db.SaveChangesAsync(token);
                    return new OrderDto { Id = order.Id, Status = order.Status };
                });
            return this.Operations(db, sender.Object);
        }
        await using var first = CreateDb(); await using var second = CreateDb();
        var request = new DeliveryOperationRequest(Guid.NewGuid(), "deliver", _now, 30, 20, 77, 1);
        var results = await Task.WhenAll(Operations(first).ExecuteAsync(request, default), Operations(second).ExecuteAsync(request, default));
        Assert.Equal(1, attempts);
        Assert.Single(results.Where(r => r.Replayed));
        await using var verify = CreateDb();
        Assert.Equal(1, await verify.EntityAuditLogs.CountAsync(a => a.EntityType == "delivery_operation"));
        Assert.Equal(OrderStatus.Delivered, (await verify.Orders.SingleAsync()).Status);
        verify.EmployeeLunches.Add(new EmployeeLunch { UserId = 1, BranchId = 7, StartedAt = _now,
            StartedByUserId = 1, CreatedAt = _now, UpdatedAt = _now });
        await verify.SaveChangesAsync();
        Assert.True((await Operations(verify).ExecuteAsync(request, default)).Replayed);
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.OrderConcurrencyException>(() =>
            Operations(verify).ExecuteAsync(request with { RecordedAt = _now.AddSeconds(-1) }, default));
    }

    [PostgreSqlIntegrationFact]
    public async Task UniqueActiveRouteAndAtomicContextRevisionAreDatabaseGuarantees()
    {
        await using var first = CreateDb(); await using var second = CreateDb();
        await Task.WhenAll(
            SenorArroz.Application.Common.Helpers.DeliveryRouteScope.AdvanceContextAsync(first, new[] { 1 }, default),
            SenorArroz.Application.Common.Helpers.DeliveryRouteScope.AdvanceContextAsync(second, new[] { 1 }, default));
        await using var verify = CreateDb();
        Assert.Equal(2, (await verify.Users.SingleAsync(u => u.Id == 1)).DeliveryContextVersion);
        verify.DeliveryRoutes.Add(new DeliveryRoute { BranchId = 7, DeliverymanId = 1,
            Status = DeliveryRouteStatus.Open, LastAssignmentAtUtc = _now });
        await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
    }

    private DeliveryAutoCompletionService CreateService(ApplicationDbContext db, ISender sender) =>
        new(
            db,
            sender,
            new FakeClock(_now),
            NullLogger<DeliveryAutoCompletionService>.Instance,
            new PostgresDeliveryAutoCompletionRouteLock(db));

    [PostgreSqlIntegrationFact]
    public async Task LegacyPreflightClosesOnlySupersededTerminalRoutesAndRollsBackOnPendingOrders()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
            "SenorArroz.Infrastructure/Scripts/enforce_delivery_route_uniqueness.sql"))) directory = directory.Parent;
        var sql = await File.ReadAllTextAsync(Path.Combine(directory!.FullName,
            "SenorArroz.Infrastructure/Scripts/enforce_delivery_route_uniqueness.sql"));
        async Task Apply() {
            await using var connection = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
            await connection.OpenAsync();
            await new Npgsql.NpgsqlCommand(sql, connection).ExecuteNonQueryAsync();
        }
        await using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync("""
            DROP INDEX ux_delivery_route_active_owner;
            UPDATE delivery_route SET created_at=now()-interval '1 day';
            UPDATE "order" SET status='delivered';
            """);
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 21, BranchId = 7, DeliverymanId = 1,
            Status = DeliveryRouteStatus.Open, LastAssignmentAtUtc = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await Apply();
        await Apply();
        var old = await db.DeliveryRoutes.AsNoTracking().SingleAsync(r => r.Id == 20);
        Assert.Equal(DeliveryRouteStatus.Completed, old.Status);
        Assert.Equal("legacy", old.ClosureSource);
        Assert.Null(old.CompletedAtUtc);
        Assert.Null(old.ActualDurationSeconds);
        Assert.Equal(1, await db.EntityAuditLogs.CountAsync(a => a.OperationType == "legacy_reconciliation"));
        Assert.Equal(OrderStatus.Delivered, (await db.Orders.AsNoTracking().SingleAsync()).Status);
        await db.Database.ExecuteSqlRawAsync("DROP INDEX ux_delivery_route_active_owner");
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 22, BranchId = 7, DeliverymanId = 1,
            Status = DeliveryRouteStatus.Open, LastAssignmentAtUtc = _now, CreatedAt = _now,
            CompletedAtUtc = _now.AddMinutes(10), ActualDurationSeconds = 600, MetSla = true });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(Apply);
        var preserved = await db.DeliveryRoutes.AsNoTracking().SingleAsync(r => r.Id == 22);
        Assert.Equal(_now.AddMinutes(10), preserved.CompletedAtUtc);
        Assert.Equal(600, preserved.ActualDurationSeconds); Assert.True(preserved.MetSla);
        await db.Database.ExecuteSqlRawAsync("UPDATE delivery_route SET completed_at_utc=null WHERE id=22");
        await db.Database.ExecuteSqlRawAsync("UPDATE \"order\" SET delivery_route_id=22,status='on_the_way'");
        await Assert.ThrowsAsync<Npgsql.PostgresException>(Apply);
        Assert.Equal(2, await db.DeliveryRoutes.CountAsync(r => r.Status == DeliveryRouteStatus.Open));
        Assert.Equal(OrderStatus.OnTheWay, (await db.Orders.AsNoTracking().SingleAsync()).Status);
    }

    private DeliveryOperationsService Operations(ApplicationDbContext db, ISender? sender = null)
    {
        var mapper = new Mock<AutoMapper.IMapper>();
        mapper.Setup(x => x.Map<List<OrderDto>>(It.IsAny<object>())).Returns(new List<OrderDto>());
        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(x => x.Id).Returns(1); actor.SetupGet(x => x.BranchId).Returns(7); actor.SetupGet(x => x.TenantId).Returns(1);
        return new DeliveryOperationsService(db, actor.Object, new FakeClock(_now), sender ?? Mock.Of<ISender>(),
            mapper.Object, new PostgresDeliveryAutoCompletionRouteLock(db));
    }

    [PostgreSqlIntegrationFact]
    public async Task OfflineConflictsPreserveAddressAndNeverDeliverCancelledReassignedOrDifferentRouteOrders()
    {
        async Task<DeliveryOperationResult> Apply(DeliveryOperationRequest operation) {
            await using var requestDb = CreateDb();
            return await Operations(requestDb).ExecuteAsync(operation, default);
        }
        await using var db = CreateDb();
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = 77, DeliverymanId = 1, BranchId = 7,
            DeviceInstallationId = "fixture", StartedAt = _now.AddHours(-1), AutoCloseAt = _now.AddHours(2),
            Status = DeliveryWorkSessionStatus.Active, LastCommunicationAt = _now });
        var address = await db.Addresses.SingleAsync();
        address.IsPrimary = true; address.DeliveryFee = 2400;
        await db.SaveChangesAsync();
        var request = new DeliveryOperationRequest(Guid.NewGuid(), "address", _now, 30, 20, 77, 1,
            address.UpdatedAt.Ticks.ToString(), System.Text.Json.JsonSerializer.SerializeToElement(new { additionalInfo = "Chofer" }));
        await db.Database.ExecuteSqlRawAsync("UPDATE address SET additional_info='Caja',updated_at=updated_at+interval '1 second'");
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.OrderConcurrencyException>(() => Apply(request));
        await db.Entry(address).ReloadAsync();
        Assert.Equal("Caja", address.AdditionalInfo);
        var corrected = request with { OperationId = Guid.NewGuid(), AddressVersion = address.UpdatedAt.Ticks.ToString() };
        await Apply(corrected);
        await db.Entry(address).ReloadAsync();
        Assert.Equal("Chofer", address.AdditionalInfo); Assert.True(address.IsPrimary); Assert.Equal(2400, address.DeliveryFee);
        var delivery = new DeliveryOperationRequest(Guid.NewGuid(), "deliver", _now, 30, 20, 77, 1);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"order\" SET status='cancelled'");
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.OrderConcurrencyException>(() => Apply(delivery));
        await db.Database.ExecuteSqlRawAsync("UPDATE \"order\" SET status='on_the_way',delivery_man_id=2");
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.OrderConcurrencyException>(() => Apply(delivery));
        await db.Database.ExecuteSqlRawAsync("UPDATE \"order\" SET delivery_man_id=1,delivery_route_id=null");
        await Assert.ThrowsAsync<SenorArroz.Domain.Exceptions.OrderConcurrencyException>(() => Apply(delivery));
        await db.Database.ExecuteSqlRawAsync("UPDATE \"order\" SET delivery_route_id=20,status='delivered'");
        Assert.False((await Apply(delivery)).Replayed);
        Assert.True((await Apply(delivery)).Replayed);
        Assert.Equal(2, await db.EntityAuditLogs.CountAsync(a => a.EntityType == "delivery_operation"));
    }

    private ApplicationDbContext CreateDb() => new(_options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);

    private DeliverymanLocation DepartureLocation(long id) => new()
    {
        Id = id,
        DeliverymanId = 1,
        DeliveryRouteId = 20,
        Latitude = 4m + (decimal)(130d / 111_195d),
        Longitude = -74m,
        AccuracyMeters = 10,
        GpsEnabled = true,
        RecordedAt = _now,
        SyncedAt = _now,
    };

    private async Task SeedAsync(ApplicationDbContext db)
    {
        db.Tenants.Add(new Tenant
        {
            Id = 1,
            Name = "Señor Arroz",
            Slug = "senor-arroz",
        });
        db.Branches.Add(new Branch
        {
            Id = 7,
            Name = "Centro",
            Address = "Sucursal",
            Phone1 = "3001234567",
            DeliveryAutoCompleteEnabled = true,
            DeliveryAutoCompleteArrivalRadiusMeters = 50,
            DeliveryAutoCompleteDepartureRadiusMeters = 120,
            DeliveryAutoCompleteMinPresenceSeconds = 15,
        });
        db.Users.AddRange(
            new User
            {
                Id = 1,
                BranchId = 7,
                Role = UserRole.Deliveryman,
                Name = "Domiciliario",
                Email = "delivery@test.local",
                Phone = "3000000001",
                PasswordHash = "test",
            },
            new User
            {
                Id = 2,
                BranchId = 7,
                Role = UserRole.Admin,
                Name = "Administrador",
                Email = "admin@test.local",
                Phone = "3000000002",
                PasswordHash = "test",
            });
        db.Customers.Add(new Customer
        {
            Id = 10,
            BranchId = 7,
            Name = "Cliente",
            Phone1 = "3000000010",
        });
        db.Neighborhoods.Add(new Neighborhood
        {
            Id = 11,
            BranchId = 7,
            Name = "Barrio",
        });
        db.Addresses.Add(new Address
        {
            Id = 30,
            CustomerId = 10,
            NeighborhoodId = 11,
            AddressText = "Destino",
            Latitude = 4m,
            Longitude = -74m,
        });
        db.DeliveryRoutes.Add(new DeliveryRoute
        {
            Id = 20,
            BranchId = 7,
            DeliverymanId = 1,
            Status = DeliveryRouteStatus.InProgress,
            LastAssignmentAtUtc = _now.AddMinutes(-10),
            StopCount = 1,
        });
        db.Orders.Add(new Order
        {
            Id = 30,
            BranchId = 7,
            TakenById = 2,
            CustomerId = 10,
            AddressId = 30,
            DeliveryManId = 1,
            DeliveryRouteId = 20,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        });
        db.DeliveryRouteStops.Add(new DeliveryRouteStop
        {
            Id = 30,
            DeliveryRouteId = 20,
            OrderId = 30,
            StopSequence = 1,
            ArrivalCandidateAtUtc = _now.AddSeconds(-30),
            ArrivalConfirmedAtUtc = _now.AddSeconds(-15),
            ArrivalEvidenceCount = 2,
            ArrivalLastSeenAtUtc = _now.AddSeconds(-15),
            ClosestDistanceMeters = 20,
        });
        await db.SaveChangesAsync();
    }
}
