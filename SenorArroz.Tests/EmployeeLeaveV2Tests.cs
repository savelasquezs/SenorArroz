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
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public sealed class EmployeeLeaveV2Tests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly FakeClock Clock = new(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        public readonly ApplicationDbContext Db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
        public readonly Mock<IEmployeeLeaveStorage> Storage = new();
        public readonly Dictionary<string, byte[]> Files = [];
        public Fixture()
        {
            Db.Branches.Add(new Branch { Id = 1, TenantId = 1, Name = "Sucursal" });
            Db.Users.AddRange(Person(1, UserRole.Kitchen), Person(2, UserRole.Admin)); Db.SaveChanges();
            Storage.Setup(x => x.UploadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, Guid key, byte[] bytes, string type, CancellationToken ct) =>
                { var path = $"private/{id}/{key}"; Files.Add(path, bytes.ToArray()); return path; });
            Storage.Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string key, int size, CancellationToken ct) => Files[key].ToArray());
            Storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns((string key, CancellationToken ct) => { Files.Remove(key); return Task.CompletedTask; });
        }
        private static User Person(int id, UserRole role) => new() { Id = id, TenantId = 1, BranchId = 1, Role = role,
            Name = $"Persona {id}", Email = $"persona{id}@example.com", Active = true, WebAccessEnabled = true, PasswordHash = "test-hash" };
        public ICurrentUser Actor(int id)
        {
            var actor = new Mock<ICurrentUser>(); actor.SetupGet(x => x.Id).Returns(id); actor.SetupGet(x => x.TenantId).Returns(1);
            actor.SetupGet(x => x.BranchId).Returns(1); actor.SetupGet(x => x.Role).Returns(id == 1 ? "Kitchen" : "Admin");
            actor.SetupGet(x => x.IsAuthenticated).Returns(true); return actor.Object;
        }
        public EmployeeLeaveCommands Commands(int id = 1)
        {
            var password = new Mock<IPasswordService>(); password.Setup(x => x.VerifyPassword("test-confirmation", "test-hash")).Returns(true);
            return new(Db, Actor(id), password.Object, Clock, new ConfigurationBuilder().Build(), Storage.Object,
                Mock.Of<IBackgroundWorkSignal<EmailOutboxWork>>(), NullLogger<EmployeeLeaveCommands>.Instance);
        }
        public EmployeeLeaveInput Input(string kind = "medical_appointment") => new() { Password = "test-confirmation",
            RequestKey = Guid.NewGuid(), Kind = kind, Justification = "Motivo de prueba", StartsAt = Clock.UtcNow.AddHours(1), EndsAt = Clock.UtcNow.AddHours(2) };
        public static EmployeeLeaveNewFile File(string code = "appointment") => new() { UploadKey = Guid.NewGuid(), EvidenceCode = code,
            FileName = "soporte.pdf", ContentType = "application/pdf", Content = Encoding.UTF8.GetBytes("%PDF-1.7\nfixture") };
        public static EmployeeLeaveAction Action(long version, string? status = null) => new() { Version = version,
            Password = "test-confirmation", Reason = "Revisión documental de prueba", Status = status, IsPaid = true };
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [Fact]
    public void CatalogKeepsTwoGroupsAndSeparatesBeforeAfterAndPeriodRecords()
    {
        Assert.Equal(15, EmployeeLeaveRules.Kinds.Count);
        Assert.Equal(10, EmployeeLeaveRules.Kinds.Count(x => x.Remuneration == "paid"));
        Assert.Equal(5, EmployeeLeaveRules.Kinds.Count(x => x.Remuneration == "unpaid"));
        Assert.All(EmployeeLeaveRules.Kind("medical_emergency").Evidence!, x => Assert.NotEqual("before", x.Stage));
        Assert.All(EmployeeLeaveRules.Kind("bereavement").Evidence!, x => Assert.Equal("after", x.Stage));
        Assert.True(EmployeeLeaveRules.Kind("lactation").IsPeriodRecord);
        Assert.DoesNotContain(EmployeeLeaveRules.Kind("lactation").Evidence!, x => x.Required);
    }

    [Theory]
    [InlineData("medical_appointment")]
    [InlineData("authority_summons")]
    [InlineData("school_obligation")]
    public async Task MissingPriorEvidenceCannotBeBypassedByJsonOrCheckbox(string kind)
    {
        await using var f = new Fixture(); var input = f.Input(kind); input.SupportRequired = false;
        await Assert.ThrowsAsync<BusinessException>(() => f.Commands().Handle(new SaveEmployeeLeaveCommand(null, input), default));
        Assert.Equal("", input.Password); Assert.Empty(f.Files); Assert.Empty(f.Db.EmployeeLeaveRequests);
        Assert.Empty(f.Db.EntityAuditLogs); Assert.Empty(f.Db.EmailOutboxMessages);
    }

    [Theory]
    [InlineData("medical_emergency")]
    [InlineData("bereavement")]
    [InlineData("domestic_calamity")]
    public async TaskUrgencyDoesNotRequireAnImpossiblePriorFile(string kind)
    {
        await using var f = new Fixture();
        await f.Commands().Handle(new SaveEmployeeLeaveCommand(null, f.Input(kind)), default);
        Assert.Single(f.Db.EmployeeLeaveRequests); Assert.Empty(f.Files);
    }

    [Fact]
    public async Task MultipartSubmissionIsIdempotentAndBindsFileBytesToRequestKey()
    {
        await using var f = new Fixture(); var input = f.Input(); var file = Fixture.File();
        var id = await f.Commands().Handle(new SaveEmployeeLeaveCommand(null, input, [file]), default);
        input.Password = "test-confirmation";
        Assert.Equal(id, await f.Commands().Handle(new SaveEmployeeLeaveCommand(null, input, [file]), default));
        Assert.Single(f.Files); Assert.Single(f.Db.EmployeeLeaveRequests); Assert.Single(f.Db.EntityAuditLogs);
        var leave = await f.Db.EmployeeLeaveRequests.SingleAsync();
        Assert.True(leave.RequestedIsPaid); Assert.Equal(2, leave.PolicyVersion); Assert.Equal("pending", leave.SupportStatus);
        Assert.DoesNotContain("test-confirmation", (await f.Db.EntityAuditLogs.SingleAsync()).AfterJson!);
        file.Content = Encoding.UTF8.GetBytes("%PDF-1.7\ndifferent"); input.Password = "test-confirmation";
        await Assert.ThrowsAsync<EmployeeLeaveConflictException>(() => f.Commands().Handle(new SaveEmployeeLeaveCommand(null, input, [file]), default));
        Assert.Single(f.Files);
    }

    [Fact]
    public async Task UploadFailureLeavesNoRequestAndRemovesPreviouslyUploadedObjects()
    {
        await using var f = new Fixture(); var first = Fixture.File(); var second = Fixture.File("attendance");
        f.Storage.Setup(x => x.UploadAsync(It.IsAny<Guid>(), second.UploadKey, It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Simulated storage failure"));
        await Assert.ThrowsAsync<IOException>(() => f.Commands().Handle(new SaveEmployeeLeaveCommand(null, f.Input(), [first, second]), default));
        Assert.Empty(f.Files); Assert.Empty(f.Db.EmployeeLeaveRequests); Assert.Empty(f.Db.EmailOutboxMessages);
    }

    [Fact]
    public async Task PriorFileMustBeReviewedAndRejectedAfterFileCanBeReplacedWithoutLosingApproval()
    {
        await using var f = new Fixture();
        var id = await f.Commands().Handle(new SaveEmployeeLeaveCommand(null, f.Input(), [Fixture.File()]), default);
        await Assert.ThrowsAsync<BusinessException>(() => f.Commands(2).Handle(new ChangeEmployeeLeaveCommand(id, "decision", Fixture.Action(1, "approved")), default));
        var initial = await f.Db.EmployeeLeaveAttachments.SingleAsync(); var verify = Fixture.Action(1);
        verify.SupportStatus = "verified"; verify.AttachmentId = initial.Id;
        await f.Commands(2).Handle(new ChangeEmployeeLeaveCommand(id, "verify", verify), default);
        await f.Commands(2).Handle(new ChangeEmployeeLeaveCommand(id, "decision", Fixture.Action(2, "approved")), default);
        EmployeeLeaveUpload Upload(long version, int? replaces = null) => new() { Password = "test-confirmation", Version = version,
            UploadKey = Guid.NewGuid(), EvidenceCode = "attendance", ReplacesAttachmentId = replaces,
            FileName = "asistencia.pdf", ContentType = "application/pdf", Content = Fixture.File().Content };
        await f.Commands().Handle(new AddEmployeeLeaveAttachmentCommand(id, Upload(3)), default);
        var old = await f.Db.EmployeeLeaveAttachments.SingleAsync(x => x.EvidenceCode == "attendance");
        var reject = Fixture.Action(4); reject.SupportStatus = "rejected"; reject.AttachmentId = old.Id;
        await f.Commands(2).Handle(new ChangeEmployeeLeaveCommand(id, "verify", reject), default);
        var replacement = Upload(5, old.Id);
        await f.Commands().Handle(new AddEmployeeLeaveAttachmentCommand(id, replacement), default);
        replacement.Password = "test-confirmation";
        await f.Commands().Handle(new AddEmployeeLeaveAttachmentCommand(id, replacement), default);
        Assert.Equal("approved", (await f.Db.EmployeeLeaveRequests.SingleAsync()).Status);
        Assert.NotNull(old.SupersededAt); Assert.Equal(3, f.Files.Count);
        var row = await f.Db.EmployeeLeaveRequests.Include(x => x.Attachments).SingleAsync();
        Assert.Equal("submitted", EmployeeLeaveRules.EvidenceStates(row).Single(x => x.Code == "attendance").Status);
        Assert.Equal(2, row.Attachments.Count(x => x.SupersededAt == null));
    }

    [Fact]
    public void AllDayRequiresColombiaMidnightAndNeverAssumesWorkingHours()
    {
        var start = new DateTime(2026, 10, 20, 5, 0, 0, DateTimeKind.Utc);
        EmployeeLeaveRules.ValidateDates(start, start.AddDays(1), true);
        Assert.Throws<BusinessException>(() => EmployeeLeaveRules.ValidateDates(start.AddHours(1), start.AddDays(1), true));
        Assert.Throws<BusinessException>(() => EmployeeLeaveRules.ValidateDates(start, start, true));
        EmployeeLeaveRules.ValidateDates(start.AddHours(22), start.AddHours(26), false);
    }

    [Fact]
    public async Task RemunerationCannotBeSpoofedAndFullDayDoesNotMatchFollowingDayFilter()
    {
        await using var f = new Fixture(); var wrong = f.Input("other"); wrong.RequestedIsPaid = true;
        await Assert.ThrowsAsync<BusinessException>(() => f.Commands().Handle(new SaveEmployeeLeaveCommand(null, wrong), default));
        var input = f.Input("personal_errand"); input.IsAllDay = true;
        input.StartsAt = new DateTime(2026, 10, 20, 5, 0, 0, DateTimeKind.Utc); input.EndsAt = input.StartsAt.AddDays(1);
        await f.Commands().Handle(new SaveEmployeeLeaveCommand(null, input), default);
        var query = new EmployeeLeaveQueries(f.Db, f.Actor(1), f.Clock);
        Assert.Empty((await query.Handle(new GetEmployeeLeavesQuery(From: input.EndsAt), default)).Items);
        var found = await query.Handle(new GetEmployeeLeavesQuery(Remunerated: false), default);
        Assert.True(Assert.Single(found.Items).IsAllDay);
    }
}
