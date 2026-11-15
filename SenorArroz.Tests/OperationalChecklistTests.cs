using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.OperationalChecklist;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public sealed class OperationalChecklistTests
{
    private static readonly DateTime Sunday = new(2026, 11, 15, 23, 0, 0, DateTimeKind.Utc);
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    private static OperationalReminder Reminder(int id = 1, int branch = 1) => new() {
        Id = id, TenantId = 1, BranchId = branch, Code = "T-" + id, Title = "Revisar nevera", Description = "Pedir si hace falta.",
        Condition = "Si corresponde", State = new() { TenantId = 1, BranchId = branch, ReminderId = id } };
    private static OperationalChecklistHandlers Handlers(ApplicationDbContext db, DateTime? now = null, string role = "Cashier", int branchId = 1,
        Mock<IReminderDocumentationSync>? sync = null)
    {
        var actor = new Mock<ICurrentUser>(); actor.SetupGet(x => x.IsAuthenticated).Returns(true);
        actor.SetupGet(x => x.TenantId).Returns(1); actor.SetupGet(x => x.Role).Returns(role);
        var branch = new Mock<IBranchContext>(); branch.Setup(x => x.RequireBranch(It.IsAny<int?>())).Returns(branchId);
        sync ??= new(); sync.Setup(x => x.SyncAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ReminderSyncResult("pending", "Test"));
        return new(db, actor.Object, branch.Object, new FakeClock(now ?? Sunday), sync.Object,
            Mock.Of<IOrderNotificationService>(), NullLogger<OperationalChecklistHandlers>.Instance);
    }
    private static SaveReminderDto Input(int? id = null, long version = 0) => new(id, "Inventario", "Revisar existencias.", "",
        "evening", [0], false, "cashier", "/operacion/cajero/insumos-proveedores", 20, true, version);

    [Fact]
    public async Task TodayIsBranchScopedAndConditionalTasksAreIncluded()
    {
        await using var db = Db(); db.OperationalReminders.AddRange(Reminder(), Reminder(2, 2)); await db.SaveChangesAsync();
        var today = await Handlers(db).Handle(new GetChecklistQuery(), default);
        Assert.Single(today.Items); Assert.Equal(1, today.BranchId); Assert.Equal(new DateOnly(2026,11,15), today.OperationDate);
        Assert.Equal("Si corresponde", today.Items[0].Reminder.Condition); Assert.False(today.Items[0].IsChecked);
        Assert.Equal(new DateTime(2026,11,15,15,30,0,DateTimeKind.Utc), today.MorningAtUtc);
        Assert.Equal(new DateTime(2026,11,15,23,0,0,DateTimeKind.Utc), today.EveningAtUtc);
    }
    [Fact]
    public async Task SharedStateIsIdempotentAndResetsByColombiaDateWithoutHistory()
    {
        await using var db = Db(); db.OperationalReminders.Add(Reminder()); await db.SaveChangesAsync();
        var query = new ChangeReminderStateCommand(1, new(new(2026,11,15), true, 0));
        var first = await Handlers(db).Handle(query, default);
        var retry = await Handlers(db, role: "Admin").Handle(query, default);
        Assert.True(retry.IsChecked); Assert.Equal(first.StateRevision, retry.StateRevision);
        var beforeMidnight = await Handlers(db, new(2026,11,16,4,59,0,DateTimeKind.Utc)).Handle(new GetChecklistQuery(), default);
        var nextDay = await Handlers(db, new(2026,11,16,5,0,0,DateTimeKind.Utc)).Handle(new GetChecklistQuery(), default);
        Assert.True(beforeMidnight.Items[0].IsChecked); Assert.False(nextDay.Items[0].IsChecked);
        Assert.Single(db.OperationalReminderStates);
    }
    [Fact]
    public async Task StaleDayAndVersionCannotOverwriteSharedState()
    {
        await using var db = Db(); db.OperationalReminders.Add(Reminder()); await db.SaveChangesAsync();
        var h = Handlers(db);
        await Assert.ThrowsAsync<ReminderConflictException>(() => h.Handle(new ChangeReminderStateCommand(1, new(new(2026,11,14), true, 0)), default));
        await h.Handle(new ChangeReminderStateCommand(1, new(new(2026,11,15), true, 0)), default);
        await Assert.ThrowsAsync<ReminderConflictException>(() => h.Handle(new ChangeReminderStateCommand(1, new(new(2026,11,15), false, 0)), default));
        Assert.True((await h.Handle(new GetChecklistQuery(), default)).Items[0].IsChecked);
    }
    [Fact]
    public async Task AnotherBranchCannotBeMarkedAndCashierCannotEditConfiguration()
    {
        await using var db = Db(); db.OperationalReminders.Add(Reminder(2, 2)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => Handlers(db).Handle(new ChangeReminderStateCommand(2, new(new(2026,11,15), true, 0)), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handlers(db).Handle(new SaveReminderCommand(Input()), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Handlers(db, role: "Kitchen").Handle(new GetChecklistQuery(), default));
    }
    [Fact]
    public async Task AdminCreatesDefinitionAndStateAndSaveSurvivesPendingPublication()
    {
        await using var db = Db(); var sync = new Mock<IReminderDocumentationSync>();
        var result = await Handlers(db, role: "Admin", sync: sync).Handle(new SaveReminderCommand(Input()), default);
        Assert.Equal("pending", result.Documentation.Status); Assert.Single(db.OperationalReminders); Assert.Single(db.OperationalReminderStates);
        Assert.Equal(1, db.OperationalReminders.Single().TenantId); Assert.Equal(1, result.Reminder.Revision);
        sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
        await Handlers(db, sync: sync).Handle(new ChangeReminderStateCommand(result.Reminder.Id, new(new(2026,11,15), true, 0)), default);
        sync.Verify(x => x.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task ConfigConflictAndInvalidManualUrlAreRejected()
    {
        await using var db = Db(); db.OperationalReminders.Add(Reminder()); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ReminderConflictException>(() => Handlers(db, role: "Admin").Handle(new SaveReminderCommand(Input(1, 0)), default));
        await Assert.ThrowsAsync<BusinessException>(() => Handlers(db, role: "Admin").Handle(new SaveReminderCommand(Input() with { ManualPath = "javascript:alert(1)" }), default));
        await Assert.ThrowsAsync<BusinessException>(() => Handlers(db, role: "Admin").Handle(new SaveReminderCommand(Input() with { DaysOfWeek = [3] }), default));
    }
    [Fact]
    public async Task WednesdayIsEmptyAndNotMarkable()
    {
        await using var db = Db(); db.OperationalReminders.Add(Reminder()); await db.SaveChangesAsync();
        var h = Handlers(db, new(2026,11,18,16,0,0,DateTimeKind.Utc));
        Assert.Empty((await h.Handle(new GetChecklistQuery(), default)).Items);
        await Assert.ThrowsAsync<ReminderConflictException>(() => h.Handle(new ChangeReminderStateCommand(1, new(new(2026,11,18), true, 0)), default));
    }
    [Theory]
    [InlineData(2026,11,16,false)] [InlineData(2026,11,17,true)]
    [InlineData(2026,11,23,true)] [InlineData(2026,11,24,false)]
    public void MondayHolidayMovesOnlyTheMeatReminder(int y, int m, int d, bool expected)
    {
        var item = Reminder(); item.DaysOfWeek = [1]; item.ShiftMondayHoliday = true;
        Assert.Equal(expected, ReminderSchedule.Applies(item, new(y,m,d)));
    }
    [Fact]
    public void DocumentationEscapesMdxAndDoesNotIncludeDailyChecks()
    {
        var row = Reminder(); row.Title = "<script>{alert(1)}</script> | [link]";
        row.Description = "**Texto** $0\nSegunda línea"; row.State.IsChecked = true;
        var mdx = ReminderDocumentationSync.Render([row], new Dictionary<int,string>{{1,"Santander"}});
        Assert.StartsWith(ReminderDocumentationSync.Marker, mdx);
        Assert.DoesNotContain("<script>", mdx); Assert.DoesNotContain("{alert", mdx); Assert.DoesNotContain("[link]", mdx);
        Assert.Contains("&#36;0<br />", mdx); Assert.DoesNotContain("isChecked", mdx); Assert.Contains("10:30", mdx);
    }
    [Fact]
    public async Task TenantFilterProtectsBothTables()
    {
        await using var db = Db(); db.OperationalReminders.Add(Reminder()); await db.SaveChangesAsync();
        using var scope = TestTenantContext.Default.BeginTenantScope(2);
        Assert.Empty(await db.OperationalReminders.AsNoTracking().ToListAsync());
        Assert.Empty(await db.OperationalReminderStates.AsNoTracking().ToListAsync());
    }
}
