using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.Auth.Commands;
using SenorArroz.Application.Features.Auth.DTOs;
using SenorArroz.Application.Features.Orders.DTOs;
using SenorArroz.Application.Features.Users.Commands;
using SenorArroz.Application.Features.Users.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Repositories;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class DeliveryBranchTransferRegressionTests
{
    private readonly DateTime _now = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TransferRequiresResolvedWork_ThenNewLoginSeesOnlyNewBranchAndRejectsOldSession()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        var oldSession = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = 1, Name = "Test", Slug = "test" });
        db.Branches.AddRange(new Branch { Id = 1, Name = "A", Address = "A" }, new Branch { Id = 2, Name = "B", Address = "B" });
        db.Users.Add(new User { Id = 10, BranchId = 1, Role = UserRole.Deliveryman, Active = true,
            Name = "Driver", Email = "driver@test", Phone = "3000000000", PasswordHash = "hash", ActiveSessionId = oldSession });
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 20, BranchId = 1, DeliverymanId = 10,
            Status = DeliveryRouteStatus.InProgress, LastAssignmentAtUtc = _now });
        db.Orders.Add(new Order { Id = 30, BranchId = 1, TakenById = 10, DeliveryManId = 10,
            DeliveryRouteId = 20, Status = OrderStatus.OnTheWay, Type = OrderType.Delivery });
        await db.SaveChangesAsync();
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<UserDto>(It.IsAny<object>())).Returns(new UserDto());
        mapper.Setup(m => m.Map<List<OrderDto>>(It.IsAny<object>())).Returns<object>(source =>
            ((IEnumerable<Order>)source).Select(o => new OrderDto { Id = o.Id, BranchId = o.BranchId }).ToList());
        mapper.Setup(m => m.Map<UserInfoDto>(It.IsAny<object>())).Returns(new UserInfoDto());
        var admin = Actor(1, null); admin.SetupGet(a => a.Role).Returns("Superadmin");
        var handler = new UpdateUserHandler(new UserRepository(db), mapper.Object, admin.Object, db,
            new PostgresDeliveryAutoCompletionRouteLock(db));
        var transfer = new UpdateUserCommand(10, new UpdateUserDto { BranchId = 2, Role = UserRole.Deliveryman, Email = "driver@test" });
        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(transfer, default));
        Assert.Equal(1, (await db.Users.SingleAsync()).BranchId);
        Assert.Equal(OrderStatus.OnTheWay, (await db.Orders.SingleAsync()).Status);
        var route = await db.DeliveryRoutes.SingleAsync();
        route.Status = DeliveryRouteStatus.Completed; route.ClosureSource = "return"; route.CompletedAtUtc = _now;
        await db.SaveChangesAsync();
        // A pending assignment also blocks a transfer when its route is already closed.
        await Assert.ThrowsAsync<BusinessException>(() => handler.Handle(transfer, default));
        (await db.Orders.SingleAsync()).Status = OrderStatus.Delivered;
        await db.SaveChangesAsync();
        await handler.Handle(transfer, default);
        Assert.Null((await db.Users.SingleAsync()).ActiveSessionId);
        db.ChangeTracker.Clear();
        var user = await db.Users.Include(u => u.Tenant).Include(u => u.Branch).SingleAsync();
        var auth = new Mock<IAuthRepository>();
        auth.Setup(a => a.GetUserByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        auth.Setup(a => a.ValidatePasswordAsync(user, "secret", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var jwt = new Mock<IJwtService>(); jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh");
        await new LoginHandler(auth.Object, Mock.Of<IRefreshTokenRepository>(), jwt.Object, mapper.Object,
            new FakeClock(_now), db, Mock.Of<IDeliveryAppVersionPolicy>()).Handle(new LoginCommand
            { Email = user.Email, Password = "secret", DeviceInstallationId = "new-device" }, default);
        var newSession = user.ActiveSessionId;
        Assert.NotNull(newSession); Assert.NotEqual(oldSession, newSession);
        DeliveryOperationsService Operations(Mock<ICurrentUser> actor) => new(db, actor.Object, new FakeClock(_now),
            Mock.Of<ISender>(), mapper.Object, new PostgresDeliveryAutoCompletionRouteLock(db));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Operations(Actor(1, oldSession)).ContextAsync(default));
        await Assert.ThrowsAsync<SessionReplacedException>(() => Operations(Actor(2, oldSession)).ContextAsync(default));
        var context = await Operations(Actor(2, newSession)).ContextAsync(default);
        Assert.Null(context.RouteId); Assert.Null(context.LastClosure); Assert.Empty(context.Orders);
        db.DeliveryRoutes.Add(new DeliveryRoute { Id = 21, BranchId = 2, DeliverymanId = 10,
            Status = DeliveryRouteStatus.Completed, ClosureSource = "next_route", CompletedAtUtc = _now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        context = await Operations(Actor(2, newSession)).ContextAsync(default);
        Assert.Equal(21, context.LastClosure!.RouteId);
        // Defensive read boundary for routes left by transfers performed before this fix.
        route = await db.DeliveryRoutes.SingleAsync(r => r.Id == 20);
        route.Status = DeliveryRouteStatus.InProgress; route.ClosureSource = null; route.CompletedAtUtc = null;
        await db.SaveChangesAsync();
        context = await Operations(Actor(2, newSession)).ContextAsync(default);
        Assert.Null(context.RouteId); Assert.Empty(context.Orders);
        await Operations(Actor(2, newSession)).CompleteActiveAsync("next_route", null, default);
        Assert.Equal(DeliveryRouteStatus.InProgress, route.Status);
    }

    private static Mock<ICurrentUser> Actor(int branch, Guid? session)
    {
        var actor = new Mock<ICurrentUser>();
        actor.SetupGet(a => a.Id).Returns(10); actor.SetupGet(a => a.TenantId).Returns(1);
        actor.SetupGet(a => a.BranchId).Returns(branch); actor.SetupGet(a => a.SessionId).Returns(session);
        return actor;
    }
}
