using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Orders.Commands;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class DeliveryAutoCompletionTests
{
    [Fact]
    public async Task ReliableArrivalPresenceAndDeparture_AutoDeliversAndAudits()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.EvaluateAsync(45, fixture.Now);
        var stop = fixture.Stop(30);
        Assert.Equal(1, stop.ArrivalEvidenceCount);
        Assert.Null(stop.ArrivalConfirmedAtUtc);

        await fixture.EvaluateAsync(30, fixture.Now.AddSeconds(30));
        Assert.Equal(2, stop.ArrivalEvidenceCount);
        Assert.NotNull(stop.ArrivalConfirmedAtUtc);

        var departure = await fixture.EvaluateAsync(130, fixture.Now.AddSeconds(60));

        Assert.Equal(OrderStatus.Delivered, fixture.Order(30).Status);
        Assert.Single(fixture.Commands);
        Assert.True(fixture.Commands[0].IsAutomaticDelivery);
        Assert.Equal(departure.Id, stop.AutoDeliveryTriggerLocationId);
        Assert.NotNull(stop.AutoDeliveredAtUtc);
        Assert.True(stop.AutoDeliveryDepartureDistanceMeters >= 120);
    }

    [Fact]
    public async Task SingleArrivalEvidenceFollowedByDeparture_ResetsWithoutDelivery()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.EvaluateAsync(40, fixture.Now);
        await fixture.EvaluateAsync(140, fixture.Now.AddSeconds(30));

        var stop = fixture.Stop(30);
        Assert.Null(stop.ArrivalCandidateAtUtc);
        Assert.Equal(0, stop.ArrivalEvidenceCount);
        Assert.Equal(OrderStatus.OnTheWay, fixture.Order(30).Status);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task Hysteresis_DoesNotDeliverUntilDepartureRadiusIsReached()
    {
        await using var fixture = await Fixture.CreateAsync();
        var samples = new[] { 45d, 35d, 60d, 95d, 110d, 119d };

        for (var index = 0; index < samples.Length; index++)
            await fixture.EvaluateAsync(samples[index], fixture.Now.AddSeconds(index * 30));

        Assert.Equal(OrderStatus.OnTheWay, fixture.Order(30).Status);
        Assert.Empty(fixture.Commands);

        await fixture.EvaluateAsync(125, fixture.Now.AddSeconds(180));

        Assert.Equal(OrderStatus.Delivered, fixture.Order(30).Status);
        Assert.Single(fixture.Commands);
    }

    [Fact]
    public async Task UnreliableGps_IsIgnoredWithoutResettingValidEvidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.EvaluateAsync(45, fixture.Now);

        await fixture.EvaluateAsync(20, fixture.Now.AddSeconds(30), accuracyMeters: 80);
        await fixture.EvaluateAsync(140, fixture.Now.AddSeconds(60), gpsEnabled: false);

        var stop = fixture.Stop(30);
        Assert.Equal(1, stop.ArrivalEvidenceCount);
        Assert.NotNull(stop.ArrivalCandidateAtUtc);
        Assert.Empty(fixture.Commands);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1d)]
    [InlineData(50.1d)]
    public async Task MissingNegativeOrExcessiveAccuracy_IsIgnored(double? accuracyMeters)
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.EvaluateAsync(20, fixture.Now, accuracyMeters);

        Assert.Null(fixture.Stop(30).ArrivalCandidateAtUtc);
        Assert.Empty(fixture.Commands);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered, 1)]
    [InlineData(OrderStatus.Cancelled, 1)]
    [InlineData(OrderStatus.OnTheWay, 99)]
    public async Task ChangedOrderBeforeDeparture_IsNoOp(OrderStatus status, int deliverymanId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfirmArrivalAsync();
        var order = fixture.Order(30);
        order.Status = status;
        order.DeliveryManId = deliverymanId;
        await fixture.Db.SaveChangesAsync();

        await fixture.EvaluateAsync(140, fixture.Now.AddSeconds(60));

        Assert.Empty(fixture.Commands);
        Assert.Null(fixture.Stop(30).AutoDeliveredAtUtc);
    }

    [Fact]
    public async Task StaleOfflinePoint_IsPersistedButCannotChangeArrivalState()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Clock.UtcNow = fixture.Now;

        var point = await fixture.EvaluateAsync(20, fixture.Now.AddMinutes(-10));

        Assert.NotNull(await fixture.Db.DeliverymanLocations.FindAsync(point.Id));
        Assert.Null(fixture.Stop(30).ArrivalCandidateAtUtc);
        Assert.Empty(fixture.Commands);
    }

    [Fact]
    public async Task NearbyStops_OnlyFirstSequenceAccumulatesAndDelivers()
    {
        await using var fixture = await Fixture.CreateAsync(secondNearbyOrder: true);

        await fixture.ConfirmArrivalAsync();

        Assert.NotNull(fixture.Stop(30).ArrivalConfirmedAtUtc);
        Assert.Null(fixture.Stop(31).ArrivalCandidateAtUtc);

        await fixture.EvaluateAsync(130, fixture.Now.AddSeconds(60));

        Assert.Equal(OrderStatus.Delivered, fixture.Order(30).Status);
        Assert.Equal(OrderStatus.OnTheWay, fixture.Order(31).Status);
        Assert.Single(fixture.Commands);
    }

    [Fact]
    public async Task MissingCoordinatesOrDisabledFeature_DoesNothing()
    {
        await using var missingCoordinates = await Fixture.CreateAsync(hasCoordinates: false);
        await missingCoordinates.EvaluateAsync(20, missingCoordinates.Now);
        Assert.Empty(missingCoordinates.Commands);

        await using var disabled = await Fixture.CreateAsync(enabled: false);
        await disabled.EvaluateAsync(20, disabled.Now);
        Assert.Empty(disabled.Commands);
        Assert.Null(disabled.Stop(30).ArrivalCandidateAtUtc);
    }

    [Fact]
    public async Task ConfirmedArrival_SurvivesDbContextReload()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using (var first = await Fixture.CreateAsync(databaseName: databaseName))
            await first.ConfirmArrivalAsync();

        await using var reloaded = await Fixture.OpenAsync(databaseName);
        await reloaded.EvaluateAsync(130, reloaded.Now.AddSeconds(60));

        Assert.Equal(OrderStatus.Delivered, reloaded.Order(30).Status);
        Assert.NotNull(reloaded.Stop(30).AutoDeliveredAtUtc);
    }

    [Fact]
    public async Task DuplicateLocation_DoesNotIncreaseEvidenceOrDeliverTwice()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.EvaluateAsync(40, fixture.Now);

        await fixture.Service.EvaluateLocationAsync(first);

        Assert.Equal(1, fixture.Stop(30).ArrivalEvidenceCount);
        await fixture.EvaluateAsync(30, fixture.Now.AddSeconds(30));
        await fixture.EvaluateAsync(130, fixture.Now.AddSeconds(60));
        Assert.Single(fixture.Commands);
    }

    [Fact]
    public async Task AutomaticCommand_UsesManualDeliverySideEffectsAndRealtimeEvent()
    {
        var order = new Order
        {
            Id = 30,
            BranchId = 7,
            TakenById = 2,
            CustomerId = 5,
            DeliveryManId = 1,
            DeliveryRouteId = 20,
            Type = OrderType.Delivery,
            Status = OrderStatus.OnTheWay,
        };
        var repository = new Mock<IOrderRepository>();
        repository.Setup(x => x.GetByIdAsync(30, It.IsAny<CancellationToken>())).ReturnsAsync(order);
        repository.Setup(x => x.ChangeStatusAsync(30, OrderStatus.Delivered, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Delivered;
                return order;
            });
        var mapper = new Mock<IMapper>();
        mapper.Setup(x => x.Map<OrderDto>(It.IsAny<object>())).Returns(() => new OrderDto
        {
            Id = order.Id,
            BranchId = order.BranchId,
            Status = order.Status,
            DeliveryManId = order.DeliveryManId,
            DeliveryRouteId = order.DeliveryRouteId,
        });
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Role).Returns(Roles.Deliveryman);
        currentUser.SetupGet(x => x.Id).Returns(1);
        currentUser.SetupGet(x => x.BranchId).Returns(7);
        var notifications = new Mock<IOrderNotificationService>();
        var workflow = new Mock<IDeliveryRouteWorkflowService>();
        var loyalty = new Mock<ILoyaltyCycleService>();
        var handler = new ChangeOrderStatusHandler(
            repository.Object,
            mapper.Object,
            currentUser.Object,
            Mock.Of<IOrderBusinessRulesService>(),
            notifications.Object,
            workflow.Object,
            Mock.Of<IPrintQueueService>(),
            loyalty.Object,
            Mock.Of<ILogger<ChangeOrderStatusHandler>>());

        await handler.Handle(new ChangeOrderStatusCommand
        {
            Id = 30,
            StatusChange = new ChangeOrderStatusDto { Status = OrderStatus.Delivered },
            IsAutomaticDelivery = true,
        }, default);

        loyalty.Verify(x => x.OnOrderDeliveredAsync(30, 7, 5, It.IsAny<CancellationToken>()), Times.Once);
        workflow.Verify(x => x.TryFinalizeRouteWhenAllTerminalAsync(30, 20, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(x => x.NotifyOrderModifiedToDelivery(
            It.Is<OrderDto>(dto => dto.Status == OrderStatus.Delivered),
            "status",
            null), Times.Once);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private long _locationId;

        private Fixture(ApplicationDbContext db, FakeClock clock, Mock<ISender> sender)
        {
            Db = db;
            Clock = clock;
            Sender = sender;
            _locationId = (db.DeliverymanLocations.Select(x => (long?)x.Id).Max() ?? 99) + 1;
            Service = new DeliveryAutoCompletionService(
                db,
                sender.Object,
                clock,
                Mock.Of<ILogger<DeliveryAutoCompletionService>>(),
                new InlineRouteLock());
            sender.Setup(x => x.Send(It.IsAny<ChangeOrderStatusCommand>(), It.IsAny<CancellationToken>()))
                .Returns<ChangeOrderStatusCommand, CancellationToken>(async (command, cancellationToken) =>
                {
                    Commands.Add(command);
                    var order = await Db.Orders.FirstAsync(x => x.Id == command.Id, cancellationToken);
                    var routeStop = await Db.DeliveryRouteStops.FirstAsync(
                        x => x.OrderId == command.Id,
                        cancellationToken);
                    order.Status = command.StatusChange.Status;
                    routeStop.AutoDeliveredAtUtc = command.AutoDeliveredAtUtc;
                    routeStop.AutoDeliveryTriggerLocationId = command.AutoDeliveryTriggerLocationId;
                    routeStop.AutoDeliveryDepartureDistanceMeters = command.AutoDeliveryDepartureDistanceMeters;
                    await Db.SaveChangesAsync(cancellationToken);
                    return new OrderDto
                    {
                        Id = order.Id,
                        BranchId = order.BranchId,
                        Status = order.Status,
                        DeliveryManId = order.DeliveryManId,
                        DeliveryRouteId = order.DeliveryRouteId,
                    };
                });
        }

        public DateTime Now { get; } = new(2026, 8, 13, 18, 0, 0, DateTimeKind.Utc);
        public ApplicationDbContext Db { get; }
        public FakeClock Clock { get; }
        public Mock<ISender> Sender { get; }
        public DeliveryAutoCompletionService Service { get; }
        public List<ChangeOrderStatusCommand> Commands { get; } = [];

        public static async Task<Fixture> CreateAsync(
            bool enabled = true,
            bool hasCoordinates = true,
            bool secondNearbyOrder = false,
            string? databaseName = null)
        {
            var fixture = New(databaseName ?? Guid.NewGuid().ToString());
            fixture.Db.Branches.Add(new Branch
            {
                Id = 7,
                Name = "Centro",
                Address = "Sucursal",
                Phone1 = "3001234567",
                DeliveryAutoCompleteEnabled = enabled,
                DeliveryAutoCompleteArrivalRadiusMeters = 50,
                DeliveryAutoCompleteDepartureRadiusMeters = 120,
                DeliveryAutoCompleteMinPresenceSeconds = 15,
            });
            fixture.Db.DeliveryRoutes.Add(new DeliveryRoute
            {
                Id = 20,
                BranchId = 7,
                DeliverymanId = 1,
                Status = DeliveryRouteStatus.InProgress,
                LastAssignmentAtUtc = fixture.Now,
            });
            fixture.AddOrder(30, 1, hasCoordinates);
            if (secondNearbyOrder)
                fixture.AddOrder(31, 2, true);
            await fixture.Db.SaveChangesAsync();
            return fixture;
        }

        public static Task<Fixture> OpenAsync(string databaseName) =>
            Task.FromResult(New(databaseName));

        public async Task ConfirmArrivalAsync()
        {
            await EvaluateAsync(45, Now);
            await EvaluateAsync(30, Now.AddSeconds(30));
        }

        public async Task<DeliverymanLocation> EvaluateAsync(
            double distanceMeters,
            DateTime recordedAt,
            double? accuracyMeters = 10,
            bool? gpsEnabled = true)
        {
            Clock.UtcNow = recordedAt > Now.AddMinutes(-2) ? recordedAt : Now;
            var location = new DeliverymanLocation
            {
                Id = _locationId++,
                DeliverymanId = 1,
                DeliveryRouteId = 20,
                ClientPointId = Guid.NewGuid(),
                Latitude = LatitudeAtDistance(distanceMeters),
                Longitude = -74m,
                AccuracyMeters = accuracyMeters,
                GpsEnabled = gpsEnabled,
                RecordedAt = recordedAt,
                SyncedAt = Clock.UtcNow,
            };
            Db.DeliverymanLocations.Add(location);
            await Db.SaveChangesAsync();
            await Service.EvaluateLocationAsync(location);
            return location;
        }

        public Order Order(int orderId) => Db.Orders.Single(x => x.Id == orderId);
        public DeliveryRouteStop Stop(int orderId) => Db.DeliveryRouteStops.Single(x => x.OrderId == orderId);

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static Fixture New(string databaseName)
        {
            var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseInMemoryDatabase(databaseName)
                    .Options);
            var clock = new FakeClock(new DateTime(2026, 8, 13, 18, 0, 0, DateTimeKind.Utc));
            return new Fixture(db, clock, new Mock<ISender>());
        }

        private void AddOrder(int orderId, int sequence, bool hasCoordinates)
        {
            Db.Addresses.Add(new Address
            {
                Id = orderId,
                CustomerId = 10,
                NeighborhoodId = 11,
                AddressText = $"Destino {orderId}",
                Latitude = hasCoordinates ? 4m : null,
                Longitude = hasCoordinates ? -74m : null,
            });
            Db.Orders.Add(new Order
            {
                Id = orderId,
                BranchId = 7,
                TakenById = 2,
                AddressId = orderId,
                DeliveryManId = 1,
                DeliveryRouteId = 20,
                Type = OrderType.Delivery,
                Status = OrderStatus.OnTheWay,
            });
            Db.DeliveryRouteStops.Add(new DeliveryRouteStop
            {
                Id = orderId,
                DeliveryRouteId = 20,
                OrderId = orderId,
                StopSequence = sequence,
            });
        }

        private static decimal LatitudeAtDistance(double meters) =>
            4m + (decimal)(meters / 111_195d);

        private sealed class InlineRouteLock : IDeliveryAutoCompletionRouteLock
        {
            public Task ExecuteAsync(
                int routeId,
                Func<CancellationToken, Task> action,
                CancellationToken cancellationToken = default) =>
                action(cancellationToken);
        }
    }
}
