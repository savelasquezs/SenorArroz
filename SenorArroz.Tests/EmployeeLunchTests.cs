using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.EmployeeLunches;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;
using SenorArroz.API.Filters;
using SenorArroz.Infrastructure.Repositories;

namespace SenorArroz.Tests;

public sealed class EmployeeLunchTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 17, 0, 0, DateTimeKind.Utc);
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    private static User Employee(int id = 1, UserRole role = UserRole.Cashier, int branch = 1) =>
        new() { Id = id, BranchId = branch, Role = role, Name = $"Employee {id}", PasswordHash = "hash", WebAccessEnabled = role != UserRole.Kitchen };
    private static ICurrentUser Actor(int id = 1, string role = "Cashier") {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(x => x.Id).Returns(id); mock.SetupGet(x => x.Role).Returns(role);
        mock.SetupGet(x => x.BranchId).Returns(1); mock.SetupGet(x => x.TenantId).Returns(1);
        mock.SetupGet(x => x.IsAuthenticated).Returns(true);
        return mock.Object;
    }
    private static IBranchContext Branch() {
        var mock = new Mock<IBranchContext>();
        mock.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(1);
        mock.Setup(x => x.EnsureAccess(It.Is<int>(x => x != 1))).Throws<UnauthorizedAccessException>();
        return mock.Object;
    }
    private static EmployeeLunchCommands Commands(ApplicationDbContext db, ICurrentUser? actor = null, FakeClock? clock = null) {
        var passwords = new Mock<IPasswordService>();
        passwords.Setup(x => x.VerifyPassword("correct", "hash")).Returns(true);
        return new(db, actor ?? Actor(), Branch(), passwords.Object, clock ?? new(Now),
            Mock.Of<IOrderNotificationService>(), NullLogger<EmployeeLunchCommands>.Instance);
    }

    [Fact]
    public void LunchTableAndColumnsMatchProductionSql()
    {
        using var db = Db();
        var entity = db.Model.FindEntityType(typeof(EmployeeLunch))!;
        Assert.Equal("employee_lunch", entity.GetTableName());
        var table = StoreObjectIdentifier.Table("employee_lunch", null);
        Assert.Equal("id", entity.FindProperty(nameof(EmployeeLunch.Id))?.GetColumnName(table));
        Assert.Equal("tenant_id", entity.FindProperty(nameof(EmployeeLunch.TenantId))?.GetColumnName(table));
        Assert.Equal("user_id", entity.FindProperty(nameof(EmployeeLunch.UserId))?.GetColumnName(table));
        Assert.Equal("started_at", entity.FindProperty(nameof(EmployeeLunch.StartedAt))?.GetColumnName(table));
        Assert.Equal("ended_at", entity.FindProperty(nameof(EmployeeLunch.EndedAt))?.GetColumnName(table));
    }

    [Fact]
    public async Task PersonalLunchUsesServerTimeAndDoesNotModifySessionOrPayroll() {
        await using var db = Db();
        var user = Employee(); user.ActiveSessionId = Guid.NewGuid(); user.PayrollExpenseId = 9;
        db.Users.Add(user); await db.SaveChangesAsync();
        var session = user.ActiveSessionId;
        var clock = new FakeClock(Now);
        var command = Commands(db, clock: clock);
        var start = await command.Handle(new ChangeEmployeeLunchCommand(false, "correct"), default);
        clock.UtcNow = Now.AddMinutes(42);
        var end = await command.Handle(new ChangeEmployeeLunchCommand(true, "correct", LunchId: start.Id), default);
        Assert.Equal(Now, end.StartedAt); Assert.Equal(clock.UtcNow, end.EndedAt);
        Assert.Equal(session, user.ActiveSessionId); Assert.Equal(9, user.PayrollExpenseId);
        Assert.Equal(1, (await db.EmployeeLunches.SingleAsync()).EndedByUserId);
    }

    [Fact]
    public async Task KitchenEmployeesLunchIndependentlyWithoutChangingScreenAccount() {
        await using var db = Db();
        db.Users.AddRange(Employee(1, UserRole.Kitchen), Employee(2, UserRole.Kitchen), Employee(3, UserRole.Kitchen));
        await db.SaveChangesAsync();
        var screen = Commands(db, Actor(10, "Kitchen"));
        foreach (var id in new[] { 1, 2, 3 }) await screen.Handle(new ChangeEmployeeLunchCommand(false, "correct", id), default);
        Assert.Equal(3, await db.EmployeeLunches.CountAsync());
        Assert.All(await db.EmployeeLunches.ToListAsync(), x => Assert.Equal(10, x.StartedByUserId));
    }

    [Theory]
    [InlineData("wrong", null)]
    [InlineData("correct", 2)]
    public async Task PersonalAccountCannotUseWrongPasswordOrAnotherIdentity(string password, int? target) {
        await using var db = Db(); db.Users.AddRange(Employee(), Employee(2)); await db.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => Commands(db).Handle(new ChangeEmployeeLunchCommand(false, password, target), default));
        Assert.Empty(await db.EmployeeLunches.ToListAsync());
    }

    [Theory]
    [InlineData(2, false, 2)]
    [InlineData(1, true, 2)]
    public async Task KitchenCannotUseAnotherBranchOrSharedScreen(int branch, bool web, int id) {
        await using var db = Db();
        var employee = Employee(id, UserRole.Kitchen, branch); employee.WebAccessEnabled = web;
        db.Users.Add(employee); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Commands(db, Actor(10, "Kitchen")).Handle(new ChangeEmployeeLunchCommand(false, "correct", id), default));
    }

    [Fact]
    public async Task PendingOrdersPreventLunchAndStaleFinishCannotCloseAnotherLunch() {
        await using var db = Db(); db.Users.Add(Employee());
        db.Orders.Add(new Order { Id = 1, BranchId = 1, DeliveryManId = 1, Status = OrderStatus.Ready, Type = OrderType.Delivery });
        await db.SaveChangesAsync();
        var commands = Commands(db);
        await Assert.ThrowsAsync<BusinessException>(() => commands.Handle(new ChangeEmployeeLunchCommand(false, "correct"), default));
        db.Orders.RemoveRange(db.Orders); await db.SaveChangesAsync();
        var lunch = await commands.Handle(new ChangeEmployeeLunchCommand(false, "correct"), default);
        await Assert.ThrowsAsync<BusinessException>(() => commands.Handle(new ChangeEmployeeLunchCommand(false, "correct"), default));
        await Assert.ThrowsAsync<BusinessException>(() => commands.Handle(new ChangeEmployeeLunchCommand(true, "correct", LunchId: lunch.Id + 1), default));
        Assert.Null((await db.EmployeeLunches.SingleAsync()).EndedAt);
    }

    [Fact]
    public async Task AdministrativeCorrectionRequiresReasonAndRecordsBeforeAndAfter() {
        await using var db = Db(); db.Users.Add(Employee()); await db.SaveChangesAsync();
        var started = await Commands(db).Handle(new ChangeEmployeeLunchCommand(false, "correct"), default);
        var admin = Commands(db, Actor(2, "Admin"), new FakeClock(Now.AddHours(1)));
        await Assert.ThrowsAsync<BusinessException>(() => admin.Handle(new CorrectEmployeeLunchCommand(started.Id, Now, Now.AddMinutes(40), started.UpdatedAt, ""), default));
        await admin.Handle(new CorrectEmployeeLunchCommand(started.Id, Now, Now.AddMinutes(40), started.UpdatedAt, "Olvidó registrar el regreso"), default);
        var audit = await db.EntityAuditLogs.SingleAsync();
        Assert.Equal("employee_lunch", audit.EntityType); Assert.NotEqual(audit.BeforeJson, audit.AfterJson);
        Assert.Equal(2, audit.ChangedByUserId);
        await Assert.ThrowsAsync<OrderConcurrencyException>(() => admin.Handle(new CorrectEmployeeLunchCommand(started.Id, Now, Now.AddMinutes(41), started.UpdatedAt, "Cambio obsoleto"), default));
    }

    [Fact]
    public async Task LunchQueriesRespectTenantBranchAndHideSharedKitchenAccounts() {
        await using var db = Db();
        db.Users.AddRange(Employee(), Employee(2, branch: 2), Employee(3, UserRole.Kitchen));
        var screen = Employee(4, UserRole.Kitchen); screen.WebAccessEnabled = true; db.Users.Add(screen);
        await db.SaveChangesAsync();
        using (TestTenantContext.Default.BeginTenantScope(2)) { db.Users.Add(Employee(5)); await db.SaveChangesAsync(); }
        var queries = new EmployeeLunchQueries(db, Actor(10, "Admin"), Branch(), new FakeClock(Now));
        var result = await queries.Handle(new GetBranchLunchesQuery(), default);
        Assert.Equal(new[] { 1, 3 }, result.Items.Select(x => x.UserId));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new EmployeeLunchQueries(db, Actor(), Branch(), new FakeClock(Now))
            .Handle(new GetBranchLunchesQuery(), default));
    }

    [Fact]
    public async Task BranchHistoryFollowsRecordedBranchAfterEmployeeTransfer() {
        await using var db = Db();
        db.Users.AddRange(Employee(1, branch: 2), Employee(2, branch: 1));
        db.EmployeeLunches.AddRange(
            new EmployeeLunch { UserId = 1, BranchId = 1, StartedAt = Now.AddHours(-1), EndedAt = Now, EndedByUserId = 1 },
            new EmployeeLunch { UserId = 2, BranchId = 2, StartedAt = Now.AddHours(-1), EndedAt = Now, EndedByUserId = 2 });
        await db.SaveChangesAsync();
        var result = await new EmployeeLunchQueries(db, Actor(10, "Admin"), Branch(), new FakeClock(Now))
            .Handle(new GetBranchLunchesQuery(), default);
        Assert.Single(result.Items.Single(x => x.UserId == 1).Lunches);
        Assert.Empty(result.Items.Single(x => x.UserId == 2).Lunches);
    }

    [Fact]
    public async Task StayAnalysisSplitsAtLunchAndKeepsRawCoordinates() {
        await using var db = Db();
        db.Branches.Add(new Branch { Id = 1, Name = "Branch" });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = 1, BranchId = 1, DeliverymanId = 1,
            StartedAt = Now.AddHours(-1), EndedAt = Now.AddMinutes(30), Status = DeliveryWorkSessionStatus.Closed });
        db.EmployeeLunches.Add(new EmployeeLunch { UserId = 1, BranchId = 1, StartedAt = Now.AddMinutes(5), EndedAt = Now.AddMinutes(25), EndedByUserId = 1 });
        for (var i = 0; i <= 30; i++) db.DeliverymanLocations.Add(new DeliverymanLocation {
            WorkSessionId = 1, DeliverymanId = 1, RecordedAt = Now.AddMinutes(i),
            Latitude = 6m, Longitude = -75m, GpsEnabled = true, AccuracyMeters = 5 });
        await db.SaveChangesAsync();
        await new DeliveryStayDetectionService(db, new FakeClock(Now.AddHours(1))).ProcessSessionAsync(1);
        Assert.Empty(await db.DeliveryStays.ToListAsync()); Assert.Equal(31, await db.DeliverymanLocations.CountAsync());
    }

    [Theory]
    [InlineData("Orders", "CreateOrder", "POST", false)]
    [InlineData("EmployeeLunch", "Correct", "PATCH", false)]
    [InlineData("Auth", "Logout", "POST", true)]
    [InlineData("EmployeeLunch", "Finish", "POST", true)]
    [InlineData("Deliveryman", "RecordLocation", "POST", true)]
    [InlineData("Deliveryman", "RecordDeviceEvent", "POST", true)]
    [InlineData("Deliveryman", "StartWorkSession", "POST", true)]
    [InlineData("Orders", "GetOrder", "GET", true)]
    public async Task LunchFilterBlocksWorkButAllowsReturnLogoutAndTracking(string controller, string action, string method, bool allowed) {
        await using var db = Db();
        db.EmployeeLunches.Add(new EmployeeLunch { UserId = 1, BranchId = 1, StartedAt = Now });
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext(); http.Request.Method = method;
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = action };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), descriptor),
            new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        var called = false;
        await new EmployeeLunchFilter(db, Actor()).OnActionExecutionAsync(context, () => {
            called = true;
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), new object()));
        });
        Assert.Equal(allowed, called);
        if (!allowed) Assert.Equal(409, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task DisablingKitchenWebAccessRejectsAnExistingSessionWithoutDisablingTheEmployee() {
        await using var db = Db();
        var user = Employee(1, UserRole.Kitchen); user.ActiveSessionId = Guid.NewGuid();
        db.Users.Add(user); await db.SaveChangesAsync();
        var auth = new AuthRepository(db, Mock.Of<IPasswordService>());
        Assert.False(await auth.IsSessionCurrentAsync(1, user.ActiveSessionId, "Kitchen"));
        user.WebAccessEnabled = true; await db.SaveChangesAsync();
        Assert.True(await auth.IsSessionCurrentAsync(1, user.ActiveSessionId, "Kitchen"));
        Assert.True(user.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LunchDoesNotCountAsCommunicationSilence(bool finished) {
        await using var db = Db();
        db.Branches.Add(new Branch { Id = 1, Name = "Branch", DeliveryTrackingLightIntervalSeconds = 300 });
        db.DeliveryWorkSessions.Add(new DeliveryWorkSession { Id = 1, BranchId = 1, DeliverymanId = 1,
            StartedAt = Now.AddHours(-2), LastCommunicationAt = Now.AddMinutes(-40),
            AutoCloseAt = Now.AddHours(4), Status = DeliveryWorkSessionStatus.Active });
        db.EmployeeLunches.Add(new EmployeeLunch { UserId = 1, BranchId = 1, StartedAt = Now.AddMinutes(-30),
            EndedAt = finished ? Now.AddMinutes(-1) : null, EndedByUserId = finished ? 1 : null });
        await db.SaveChangesAsync();
        await new DeliveryTrackingAlertService(db, new FakeClock(Now), Mock.Of<IFcmPushService>(),
            NullLogger<DeliveryTrackingAlertService>.Instance).ProcessAsync();
        Assert.Empty(await db.DeliveryTrackingAlerts.Where(x => x.AlertType == DeliveryTrackingAlertType.NoCommunication).ToListAsync());
    }
}
