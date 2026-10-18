using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class KitchenVoiceEventsTests
{
    private sealed class Clock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public async Task Replay_is_scoped_to_tenant_and_selected_branch_and_bootstraps_recent_events()
    {
        var tenant = new TestTenantContext();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(dbOptions,
            currentTenant: tenant, tenantExecutionContext: tenant);
        var clock = new Clock();

        using (tenant.BeginSystemScope())
        {
            db.Orders.AddRange(
                new Order { Id = 1, TenantId = 1, BranchId = 101, TakenById = 1, Type = OrderType.Onsite },
                new Order { Id = 2, TenantId = 1, BranchId = 202, TakenById = 1, Type = OrderType.Onsite },
                new Order { Id = 3, TenantId = 2, BranchId = 101, TakenById = 1, Type = OrderType.Onsite },
                new Order { Id = 4, TenantId = 1, BranchId = 101, TakenById = 1, Type = OrderType.Onsite },
                // Already transferred: old branch has a stored event that must not replay.
                new Order { Id = 5, TenantId = 1, BranchId = 202, TakenById = 1, Type = OrderType.Onsite });
            db.KitchenVoiceEvents.AddRange(
                new KitchenVoiceEvent { TenantId = 1, BranchId = 101, OrderId = 1, Kind = "new",
                    CreatedAt = clock.UtcNow.AddSeconds(-15) },
                new KitchenVoiceEvent { TenantId = 1, BranchId = 202, OrderId = 2, Kind = "new",
                    CreatedAt = clock.UtcNow.AddSeconds(-10) },
                new KitchenVoiceEvent { TenantId = 2, BranchId = 101, OrderId = 3, Kind = "new",
                    CreatedAt = clock.UtcNow.AddSeconds(-5) },
                new KitchenVoiceEvent { TenantId = 1, BranchId = 101, OrderId = 4, Kind = "reservation",
                    CreatedAt = clock.UtcNow.AddHours(-3) },
                new KitchenVoiceEvent { TenantId = 1, BranchId = 101, OrderId = 5, Kind = "new",
                    CreatedAt = clock.UtcNow.AddSeconds(-3) });
            await db.SaveChangesAsync();
        }

        var scope = new Mock<IBranchContext>();
        scope.Setup(s => s.RequireBranch(It.IsAny<int?>())).Returns((int? id) => id ?? 101);
        var controller = new OrdersController(new Mock<IMediator>().Object,
            clock, new Mock<IUserRepository>().Object,
            new Mock<ICurrentUser>().Object, scope.Object, tenant, db);

        var result = await controller.GetKitchenVoiceEvents(after: 0, branchId: 101);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            ((OkObjectResult)result).Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var replay = json.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => e.GetProperty("orderId").GetInt32()).ToArray();
        Assert.Equal(new[] { 1, 4 }, replay);

        var firstOpen = await controller.GetKitchenVoiceEvents(branchId: 101);
        using var bootstrap = JsonDocument.Parse(JsonSerializer.Serialize(
            ((OkObjectResult)firstOpen).Value!, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var latest = bootstrap.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => e.GetProperty("orderId").GetInt32()).ToArray();
        Assert.Equal(new[] { 1 }, latest);
        Assert.Equal(4, bootstrap.RootElement.GetProperty("cursor").GetInt32());
    }
}
