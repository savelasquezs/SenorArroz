using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class WhatsAppAwayMessageTests
{
    [Fact]
    public async Task BusinessHours_TreatOpeningAsOpenAndClosingAsClosed()
    {
        await using var db = Db();
        AddWeeklySchedule(db, includeWeekend: true);
        await db.SaveChangesAsync();
        var service = new BranchBusinessHoursService(db);

        var atOpening = await service.Evaluate(1, Utc(2026, 7, 27, 14, 0));
        var atClosing = await service.Evaluate(1, Utc(2026, 7, 27, 23, 0));

        Assert.True(atOpening.IsConfigured);
        Assert.True(atOpening.IsOpen);
        Assert.False(atClosing.IsOpen);
        Assert.Equal(Utc(2026, 7, 27, 23, 0), atClosing.ClosedPeriodStartedAtUtc);
        Assert.Equal(Utc(2026, 7, 28, 14, 0), atClosing.NextOpeningAtUtc);
    }

    [Fact]
    public async Task BusinessHours_BeforeMondayOpeningFindsFridayCloseAcrossWeekend()
    {
        await using var db = Db();
        AddWeeklySchedule(db, includeWeekend: false);
        await db.SaveChangesAsync();

        var result = await new BranchBusinessHoursService(db)
            .Evaluate(1, Utc(2026, 7, 27, 13, 0));

        Assert.True(result.IsConfigured);
        Assert.False(result.IsOpen);
        Assert.Equal(Utc(2026, 7, 24, 23, 0), result.ClosedPeriodStartedAtUtc);
        Assert.Equal(Utc(2026, 7, 27, 14, 0), result.NextOpeningAtUtc);
    }

    [Fact]
    public async Task BusinessHours_MissingOrAllClosedScheduleIsNotConfigured()
    {
        await using var missingDb = Db();
        var missing = await new BranchBusinessHoursService(missingDb)
            .Evaluate(1, Utc(2026, 7, 27, 13, 0));

        await using var closedDb = Db();
        foreach (var day in Enum.GetValues<DayOfWeek>())
            closedDb.BranchBusinessHours.Add(new BranchBusinessHour { BranchId = 1, DayOfWeek = day, IsClosed = true });
        await closedDb.SaveChangesAsync();
        var closed = await new BranchBusinessHoursService(closedDb)
            .Evaluate(1, Utc(2026, 7, 27, 13, 0));

        Assert.False(missing.IsConfigured);
        Assert.False(closed.IsConfigured);
    }

    [Fact]
    public void Template_RendersKnownVariablesAndRejectsUnknownOrIncompleteOnes()
    {
        var service = new WhatsAppAwayMessageService();
        var rendered = service.Render(
            "Hola {{ branchname }}. Abrimos {{NextOpening}}.",
            "Centro",
            Utc(2026, 7, 27, 23, 0),
            Utc(2026, 7, 28, 14, 30));

        Assert.Equal("Hola Centro. Abrimos mañana a las 9:30 a. m..", rendered);
        Assert.Contains("no está disponible", service.ValidateTemplate("{{CustomerName}}"));
        Assert.Contains("incompleta", service.ValidateTemplate("Hola {{BranchName"));
        Assert.Equal(
            "away:17:20260727230000",
            WhatsAppAwayMessageService.BuildDispatchKey(17, Utc(2026, 7, 27, 23, 0)));
    }

    [Fact]
    public async Task AwaySender_SendsInHumanModeAndDoesNotRepeatDispatch()
    {
        await using var db = Db();
        var branch = new Branch { Id = 1, Name = "Centro" };
        db.Branches.Add(branch);
        db.WhatsAppConversations.Add(new WhatsAppConversation
        {
            Id = 7,
            BranchId = 1,
            PhoneNumber = "573001234567",
            AttentionMode = WhatsAppAttentionMode.Human
        });
        db.WhatsAppBranchSettings.Add(new WhatsAppBranchSetting
        {
            Id = 1,
            BranchId = 1,
            PhoneNumberId = "phone-id",
            BusinessAccountId = "business-id",
            DisplayPhoneNumber = "3001234567",
            AccessToken = "token",
            WebhookVerifyToken = "verify",
            IsActive = true,
            IsVerified = true,
            AwayMessageEnabled = true,
            AwayMessageText = WhatsAppAwayMessageService.DefaultTemplate
        });
        await db.SaveChangesAsync();

        var cloud = new Mock<IWhatsAppCloudClient>();
        cloud.Setup(x => x.SendTextMessageAsync(
                "phone-id",
                "token",
                "573001234567",
                "Estamos cerrados",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppCloudSendResult(true, "wamid-away", null));
        var notifications = new Mock<IWhatsAppNotificationService>();
        notifications.Setup(x => x.NotifyMessageCreatedAsync(
                1,
                It.IsAny<WhatsAppConversationDto>(),
                It.IsAny<WhatsAppMessageDto>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var now = Utc(2026, 7, 27, 23, 5);
        var sender = new WhatsAppAutomaticMessageSender(
            db,
            cloud.Object,
            notifications.Object,
            Mock.Of<IClock>(clock => clock.UtcNow == now));

        var first = await sender.SendAwayTextAsync(7, "away:7:20260727230000", "Estamos cerrados", default);
        var repeated = await sender.SendAwayTextAsync(7, "away:7:20260727230000", "Estamos cerrados", default);

        Assert.True(first.Success);
        Assert.True(repeated.Success);
        cloud.Verify(x => x.SendTextMessageAsync(
            "phone-id",
            "token",
            "573001234567",
            "Estamos cerrados",
            It.IsAny<CancellationToken>()), Times.Once);
        var message = await db.WhatsAppMessages.SingleAsync();
        Assert.False(message.SentByAi);
        Assert.Contains("\"origin\":\"away_message\"", message.RawPayload);
        Assert.Equal(WhatsAppAttentionMode.Human, (await db.WhatsAppConversations.FindAsync(7))!.AttentionMode);
    }

    private static void AddWeeklySchedule(ApplicationDbContext db, bool includeWeekend)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var isClosed = !includeWeekend && day is DayOfWeek.Saturday or DayOfWeek.Sunday;
            db.BranchBusinessHours.Add(new BranchBusinessHour
            {
                BranchId = 1,
                DayOfWeek = day,
                IsClosed = isClosed,
                OpenTime = isClosed ? null : new TimeOnly(9, 0),
                CloseTime = isClosed ? null : new TimeOnly(18, 0),
                DisplayOrder = day == DayOfWeek.Sunday ? 6 : (int)day - 1
            });
        }
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static ApplicationDbContext Db() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
