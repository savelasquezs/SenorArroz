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

    private DeliveryAutoCompletionService CreateService(ApplicationDbContext db, ISender sender) =>
        new(
            db,
            sender,
            new FakeClock(_now),
            NullLogger<DeliveryAutoCompletionService>.Instance,
            new PostgresDeliveryAutoCompletionRouteLock(db));

    private ApplicationDbContext CreateDb() => new(_options);

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
