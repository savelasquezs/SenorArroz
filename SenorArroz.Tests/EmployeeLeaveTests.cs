using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Filters;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.EmployeeLeaves;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class EmployeeLeaveTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
    private static ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    private static User Person(int id, UserRole role = UserRole.Kitchen, int branch = 1) => new()
    {
        Id = id, TenantId = 1, BranchId = branch, Role = role, Name = $"Persona {id}",
        Email = $"persona{id}@example.com", PasswordHash = "stored-hash", Active = true, WebAccessEnabled = true
    };
    private static ICurrentUser Actor(User user, string? effectiveRole = null)
    {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(x => x.Id).Returns(user.Id);
        mock.SetupGet(x => x.TenantId).Returns(user.TenantId);
        mock.SetupGet(x => x.BranchId).Returns(user.BranchId);
        mock.SetupGet(x => x.Role).Returns(effectiveRole ?? user.Role!.Value.ToString());
        mock.SetupGet(x => x.IsAuthenticated).Returns(true);
        return mock.Object;
    }
    private static Mock<IPasswordService> Passwords()
    {
        var mock = new Mock<IPasswordService>();
        mock.Setup(x => x.VerifyPassword("correct-password", "stored-hash")).Returns(true);
        return mock;
    }
    private static EmployeeLeaveCommands Commands(ApplicationDbContext db, User user, string? effectiveRole = null) => new(
        db, Actor(user, effectiveRole), Passwords().Object, new FakeClock(Now), new ConfigurationBuilder().Build(),
        Mock.Of<IEmployeeLeaveStorage>(), Mock.Of<IBackgroundWorkSignal<EmailOutboxWork>>(), NullLogger<EmployeeLeaveCommands>.Instance);
    private static EmployeeLeaveInput Input(string password = "correct-password") => new()
    {
        RequestKey = Guid.NewGuid(), Kind = "personal_errand", StartsAt = Now.AddDays(1), EndsAt = Now.AddDays(1).AddHours(2),
        Justification = "Justificación privada del trabajador", Password = password
    };
    private static EmployeeLeaveAction Approve(long version = 1) => new()
    {
        Version = version, Password = "correct-password", Status = "approved", IsPaid = false, Reason = "Se autoriza el intervalo solicitado."
    };
    private static async Task Seed(ApplicationDbContext db, params User[] users)
    {
        db.Branches.AddRange(new Branch { Id = 1, TenantId = 1, Name = "Sucursal uno" }, new Branch { Id = 2, TenantId = 1, Name = "Sucursal dos" });
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
    }

    [Fact]
    public void PasswordConfirmationPreservesDeliverySessionAndRejectsTerminalOrInactiveAccount()
    {
        var user = Person(1, UserRole.Deliveryman); user.ActiveSessionId = Guid.NewGuid();
        var session = user.ActiveSessionId;
        EmployeePasswordConfirmation.Verify(user, "correct-password", Passwords().Object);
        Assert.Equal(session, user.ActiveSessionId);
        Assert.Throws<BusinessException>(() => EmployeePasswordConfirmation.Verify(user, "wrong", Passwords().Object));
        user.Active = false;
        Assert.Throws<UnauthorizedAccessException>(() => EmployeePasswordConfirmation.Verify(user, "correct-password", Passwords().Object));
        user.Active = true; user.AccountType = EmployeeAccountType.Terminal;
        Assert.Throws<UnauthorizedAccessException>(() => EmployeePasswordConfirmation.Verify(user, "correct-password", Passwords().Object));
    }

    [Theory]
    [InlineData(false, "Orders", "GetOrders", null, false)]
    [InlineData(false, "EmployeeLeaves", "Create", null, true)]
    [InlineData(false, "Users", "GetUser", 1, true)]
    [InlineData(false, "Users", "GetUser", 2, false)]
    [InlineData(false, "EmployeeLunch", "Branch", null, false)]
    [InlineData(false, "EmployeeLunch", "Start", null, true)]
    [InlineData(true, "Orders", "GetOrders", null, true)]
    [InlineData(true, "EmployeeLeaves", "Create", null, false)]
    [InlineData(true, "EmployeeLunch", "Branch", null, true)]
    [InlineData(true, "Users", "GetUser", 1, false)]
    public void KitchenAccountBoundaryDoesNotConfuseWebAccessWithTerminal(bool terminal, string controller, string action, int? target, bool allowed)
    {
        var user = Person(1); user.AccountType = terminal ? EmployeeAccountType.Terminal : EmployeeAccountType.Personal;
        Assert.Equal(allowed, EmployeeAccountAccess.Allows(user, controller, action, target));
        user.WebAccessEnabled = false;
        Assert.False(EmployeeAccountAccess.Allows(user, controller, action, target));
    }

    [Fact]
    public async Task WrongPasswordCreatesNeitherRequestNorAuditNorEmail()
    {
        await using var db = Db(); var user = Person(1); await Seed(db, user);
        var input = Input("wrong");
        await Assert.ThrowsAsync<BusinessException>(() => Commands(db, user).Handle(new SaveEmployeeLeaveCommand(null, input), default));
        Assert.Equal("", input.Password);
        Assert.Empty(db.EmployeeLeaveRequests); Assert.Empty(db.EntityAuditLogs); Assert.Empty(db.EmailOutboxMessages);
    }

    [Fact]
    public async Task CreationIsIdempotentAndAuditNeverContainsPassword()
    {
        await using var db = Db(); var user = Person(1); var admin = Person(2, UserRole.Admin); await Seed(db, user, admin);
        var input = Input(); var commands = Commands(db, user);
        var publicId = await commands.Handle(new SaveEmployeeLeaveCommand(null, input), default);
        Assert.Equal("", input.Password);
        input.Password = "correct-password";
        Assert.Equal(publicId, await commands.Handle(new SaveEmployeeLeaveCommand(null, input), default));
        Assert.Single(db.EmployeeLeaveRequests);
        var audit = Assert.Single(db.EntityAuditLogs);
        Assert.DoesNotContain("correct-password", audit.AfterJson ?? "");
        Assert.DoesNotContain("stored-hash", audit.AfterJson ?? "");
        Assert.Equal(2, db.EmailOutboxMessages.Count());
        Assert.All(db.EmailOutboxMessages, message => Assert.DoesNotContain(input.Justification, message.Body));
        input.Password = "correct-password"; input.Justification = "Distinto contenido con la misma clave";
        await Assert.ThrowsAsync<EmployeeLeaveConflictException>(() => commands.Handle(new SaveEmployeeLeaveCommand(null, input), default));
    }

    [Fact]
    public async Task AdminCannotApproveOwnRequestNorRequestsInAnotherBranch()
    {
        await using var db = Db(); var admin = Person(1, UserRole.Admin); var employee = Person(2, branch: 2); await Seed(db, admin, employee);
        var ownId = await Commands(db, admin).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(ownId, "decision", Approve()), default));
        var otherId = await Commands(db, employee).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(otherId, "decision", Approve()), default));
        Assert.All(db.EmployeeLeaveRequests, x => Assert.Equal("pending", x.Status));
    }

    [Fact]
    public async Task DecisionRequiresReasonCurrentPasswordAndFreshVersionAndNotifiesWorker()
    {
        await using var db = Db(); var user = Person(1); var admin = Person(2, UserRole.Admin); await Seed(db, user, admin);
        var publicId = await Commands(db, user).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        var action = Approve(); action.Password = "wrong";
        await Assert.ThrowsAsync<BusinessException>(() => Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(publicId, "decision", action), default));
        action = Approve(); action.Reason = "  ";
        await Assert.ThrowsAsync<BusinessException>(() => Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(publicId, "decision", action), default));
        await Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(publicId, "decision", Approve()), default);
        var leave = await db.EmployeeLeaveRequests.SingleAsync();
        Assert.Equal("approved", leave.Status); Assert.Equal(2, leave.Version); Assert.Equal(2, leave.DecidedByUserId);
        Assert.Equal(1, leave.LastReadVersion);
        Assert.Equal(3, await db.EmailOutboxMessages.CountAsync());
        await Assert.ThrowsAsync<EmployeeLeaveConflictException>(() => Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(publicId, "decision", Approve()), default));
        await Commands(db, user).Handle(new MarkEmployeeLeaveReadCommand(publicId, 2), default);
        Assert.Equal(2, leave.LastReadVersion);
    }

    [Fact]
    public async Task DeliveryScopedAdminTokenCannotUsePersistedAdminRoleToReview()
    {
        await using var db = Db(); var user = Person(1); var admin = Person(2, UserRole.Admin); await Seed(db, user, admin);
        var id = await Commands(db, user).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, admin, "Deliveryman").Handle(new ChangeEmployeeLeaveCommand(id, "decision", Approve()), default));
    }

    [Fact]
    public async Task ApprovedRequestCannotBeSilentlyEditedAndOverlappingApprovalIsRejected()
    {
        await using var db = Db(); var user = Person(1); var admin = Person(2, UserRole.Admin); await Seed(db, user, admin);
        var id = await Commands(db, user).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        var second = await Commands(db, user).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        await Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(id, "decision", Approve()), default);
        var edit = Input(); edit.Version = 2;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Commands(db, user).Handle(new SaveEmployeeLeaveCommand(id, edit), default));
        await Assert.ThrowsAsync<BusinessException>(() => Commands(db, admin).Handle(new ChangeEmployeeLeaveCommand(second, "decision", Approve()), default));
    }

    [Fact]
    public async Task OtherWorkersAndOtherTenantsCannotReadRequestOrAttachment()
    {
        await using var db = Db(); var user = Person(1); var other = Person(2); await Seed(db, user, other);
        var id = await Commands(db, user).Handle(new SaveEmployeeLeaveCommand(null, Input()), default);
        var query = new EmployeeLeaveQueries(db, Actor(other), new FakeClock(Now));
        await Assert.ThrowsAsync<NotFoundException>(() => query.Handle(new GetEmployeeLeaveQuery(id), default));
        await Assert.ThrowsAsync<NotFoundException>(() => Commands(db, other).Handle(new DownloadEmployeeLeaveAttachmentQuery(id, 1), default));
        using (TestTenantContext.Default.BeginTenantScope(2))
        {
            Assert.Empty(await db.EmployeeLeaveRequests.ToListAsync());
            Assert.Empty(await db.EmailOutboxMessages.ToListAsync());
        }
    }

    [Fact]
    public async Task TerminalCannotSubmitEvenWithAValidPassword()
    {
        await using var db = Db(); var screen = Person(1); screen.AccountType = EmployeeAccountType.Terminal; await Seed(db, screen);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Commands(db, screen).Handle(new SaveEmployeeLeaveCommand(null, Input()), default));
        Assert.Empty(db.EmployeeLeaveRequests);
    }

    [Theory]
    [InlineData("medical_appointment")]
    [InlineData("bereavement")]
    public void InternalPaidCategoryCannotBeApprovedAsUnpaid(string kind) =>
        Assert.Throws<BusinessException>(() => EmployeeLeaveRules.ValidateDecision("approved", kind, false));

    [Fact]
    public void FileValidationRejectsSpoofedTypesEmptyAndOversizedFiles()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nexample");
        Assert.Equal("application/pdf", EmployeeLeaveFiles.Validate("soporte.pdf", "application/pdf", pdf).ContentType);
        Assert.Throws<BusinessException>(() => EmployeeLeaveFiles.Validate("soporte.jpg", "image/jpeg", pdf));
        Assert.Throws<BusinessException>(() => EmployeeLeaveFiles.Validate("soporte.pdf", "application/pdf", []));
        Assert.Throws<BusinessException>(() => EmployeeLeaveFiles.Validate("soporte.pdf", "application/pdf", new byte[EmployeeLeaveFiles.MaxBytes + 1]));
    }
}
