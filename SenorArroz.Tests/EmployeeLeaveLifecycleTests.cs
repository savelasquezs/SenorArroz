using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.EmployeeLeaves;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class EmployeeLeaveLifecycleTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly FakeClock Clock = new(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc));
        public readonly ApplicationDbContext Db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        public readonly Mock<IEmployeeLeaveStorage> Storage = new();
        public readonly Dictionary<string, byte[]> Files = [];
        public User Worker { get; } = Person(1, UserRole.Kitchen);
        public User Admin { get; } = Person(2, UserRole.Admin);
        public User Other { get; } = Person(3, UserRole.Kitchen);
        public Fixture()
        {
            Db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "Sucursal" });
            Db.Users.AddRange(Worker, Admin, Other); Db.SaveChanges();
            Storage.Setup(x => x.UploadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid key, byte[] bytes, string type, CancellationToken ct) =>
                { var path = $"private/{id}/{key}"; Files.Add(path, bytes.ToArray()); return path; });
            Storage.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, int size, CancellationToken ct) => Files[key].ToArray());
            Storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => { Files.Remove(key); return Task.CompletedTask; });
        }
        private static User Person(int id, UserRole role) => new() { Id = id, TenantId = 1, BranchId = 1,
            Role = role, Name = $"Persona {id}", Email = $"persona{id}@example.com", Active = true, WebAccessEnabled = true, PasswordHash = "hash" };
        public ICurrentUser Actor(User user)
        {
            var actor = new Mock<ICurrentUser>();
            actor.SetupGet(x => x.Id).Returns(user.Id); actor.SetupGet(x => x.TenantId).Returns(1);
            actor.SetupGet(x => x.BranchId).Returns(1); actor.SetupGet(x => x.Role).Returns(user.Role!.Value.ToString());
            actor.SetupGet(x => x.IsAuthenticated).Returns(true); return actor.Object;
        }
        public EmployeeLeaveCommands Commands(User user)
        {
            var password = new Mock<IPasswordService>(); password.Setup(x => x.VerifyPassword("correct", "hash")).Returns(true);
            return new(Db, Actor(user), password.Object, Clock, new ConfigurationBuilder().Build(), Storage.Object,
                Mock.Of<IBackgroundWorkSignal<EmailOutboxWork>>(), NullLogger<EmployeeLeaveCommands>.Instance);
        }
        public Task<Guid> Create() => Commands(Worker).Handle(new SaveEmployeeLeaveCommand(null, new EmployeeLeaveInput
        { Password = "correct", RequestKey = Guid.NewGuid(), Kind = "other", Justification = "Motivo privado",
            StartsAt = Clock.UtcNow.AddHours(1), EndsAt = Clock.UtcNow.AddHours(2) }), default);
        public static EmployeeLeaveAction Action(long version, string? status = null) => new()
            { Password = "correct", Version = version, Reason = "Motivo confirmado", Status = status, IsPaid = false };
        public EmployeeLeaveUpload Upload(Guid key, long version = 1, string suffix = "A") => new()
            { Password = "correct", Version = version, UploadKey = key, FileName = "soporte.pdf", ContentType = "application/pdf", Content = Encoding.UTF8.GetBytes("%PDF-1.7\n" + suffix) };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Fact]
    public async Task AttachmentReplayChecksBytesAndNeverDuplicatesAuditOrStorage()
    {
        await using var f = new Fixture(); var id = await f.Create(); var key = Guid.NewGuid();
        var commands = f.Commands(f.Worker);
        await commands.Handle(new AddEmployeeLeaveAttachmentCommand(id, f.Upload(key)), default);
        await commands.Handle(new AddEmployeeLeaveAttachmentCommand(id, f.Upload(key)), default);
        Assert.Single(f.Files); Assert.Single(f.Db.EmployeeLeaveAttachments);
        Assert.Equal(2, await f.Db.EntityAuditLogs.CountAsync());
        await Assert.ThrowsAsync<EmployeeLeaveConflictException>(() => commands.Handle(new AddEmployeeLeaveAttachmentCommand(id, f.Upload(key, suffix: "B")), default));
        Assert.Single(f.Files);
        var file = await f.Db.EmployeeLeaveAttachments.SingleAsync();
        var downloaded = await commands.Handle(new DownloadEmployeeLeaveAttachmentQuery(id, file.Id), default);
        Assert.Equal(f.Upload(key).Content, downloaded.Content);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Commands(f.Other).Handle(new DownloadEmployeeLeaveAttachmentQuery(id, file.Id), default));
        await commands.Handle(new DeleteEmployeeLeaveAttachmentCommand(id, file.Id, Fixture.Action(2)), default);
        Assert.Empty(f.Files); Assert.Empty(f.Db.EmployeeLeaveAttachments);
        Assert.Equal("pending", (await f.Db.EmployeeLeaveRequests.SingleAsync()).SupportStatus);
    }

    [Fact]
    public async Task InvalidPasswordCannotUploadAndVerifiedSupportCannotBeDeletedSilently()
    {
        await using var f = new Fixture(); var id = await f.Create(); var input = f.Upload(Guid.NewGuid()); input.Password = "wrong";
        await Assert.ThrowsAsync<BusinessException>(() => f.Commands(f.Worker).Handle(new AddEmployeeLeaveAttachmentCommand(id, input), default));
        Assert.Equal("", input.Password); Assert.Empty(f.Files);
        await f.Commands(f.Worker).Handle(new AddEmployeeLeaveAttachmentCommand(id, f.Upload(Guid.NewGuid())), default);
        await f.Commands(f.Admin).Handle(new ChangeEmployeeLeaveCommand(id, "decision", Fixture.Action(2, "approved")), default);
        var verify = Fixture.Action(3); verify.SupportStatus = "verified";
        await f.Commands(f.Admin).Handle(new ChangeEmployeeLeaveCommand(id, "verify", verify), default);
        var attachment = await f.Db.EmployeeLeaveAttachments.SingleAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Commands(f.Worker).Handle(new DeleteEmployeeLeaveAttachmentCommand(id, attachment.Id, Fixture.Action(4)), default));
        Assert.Single(f.Files); Assert.Equal("verified", (await f.Db.EmployeeLeaveRequests.SingleAsync()).SupportStatus);
    }

    [Fact]
    public async Task ReturnAndVerificationUseServerTimeAndReadAcknowledgementCannotRegress()
    {
        await using var f = new Fixture(); var id = await f.Create();
        await f.Commands(f.Admin).Handle(new ChangeEmployeeLeaveCommand(id, "decision", Fixture.Action(1, "approved")), default);
        await Assert.ThrowsAsync<BusinessException>(() => f.Commands(f.Worker).Handle(new ChangeEmployeeLeaveCommand(id, "return", Fixture.Action(2)), default));
        f.Clock.UtcNow = f.Clock.UtcNow.AddHours(2);
        await f.Commands(f.Worker).Handle(new ChangeEmployeeLeaveCommand(id, "return", Fixture.Action(2)), default);
        var verify = Fixture.Action(3); verify.VerifyReturn = true;
        await f.Commands(f.Admin).Handle(new ChangeEmployeeLeaveCommand(id, "verify", verify), default);
        await f.Commands(f.Worker).Handle(new MarkEmployeeLeaveReadCommand(id, 4), default);
        await f.Commands(f.Worker).Handle(new MarkEmployeeLeaveReadCommand(id, 2), default);
        var request = await f.Db.EmployeeLeaveRequests.SingleAsync();
        Assert.Equal(f.Clock.UtcNow, request.ReturnedAt); Assert.Equal(f.Clock.UtcNow, request.ReturnVerifiedAt);
        Assert.Equal(f.Admin.Id, request.ReturnVerifiedByUserId); Assert.Equal(4, request.LastReadVersion);
        Assert.All(f.Db.EntityAuditLogs, x => Assert.DoesNotContain("\"Password\"", x.AfterJson ?? ""));
    }

    [Fact]
    public async Task HistoryPreservesActorSnapshotAndPaginationCannotOverflow()
    {
        await using var f = new Fixture(); var id = await f.Create();
        f.Worker.Name = "Nombre nuevo"; await f.Db.SaveChangesAsync();
        var queries = new EmployeeLeaveQueries(f.Db, f.Actor(f.Worker), f.Clock);
        var history = await queries.Handle(new GetEmployeeLeaveHistoryQuery(id), default);
        Assert.Equal("Persona 1", Assert.Single(history.Items).ActorName);
        var page = await queries.Handle(new GetEmployeeLeavesQuery(Page: int.MaxValue, PageSize: int.MaxValue), default);
        Assert.Equal(100000, page.Page); Assert.Empty(page.Items); Assert.Equal(1, page.TotalCount);
        var last = await queries.Handle(new GetEmployeeLeaveHistoryQuery(id, int.MaxValue, int.MaxValue), default);
        Assert.Empty(last.Items); Assert.Equal(100000, last.Page);
    }
}
