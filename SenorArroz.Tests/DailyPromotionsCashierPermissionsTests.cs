using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.DailyPromotions.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class DailyPromotionsCashierPermissionsTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 3, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Cashier_CreatesTodaysPromotion_WhenNoActivePromotionExists()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        await db.SaveChangesAsync();
        var controller = Controller(db, userId: 11);

        var action = await controller.Upsert(7, ValidDto(), default);

        Assert.IsType<OkObjectResult>(action.Result);
        var saved = Assert.Single(db.DailyPromotions);
        Assert.Equal(11, saved.CreatedByUserId);
        Assert.True(saved.IsActive);
        Assert.Equal(new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc), saved.StartsAt);
        Assert.Equal(new DateTime(2026, 8, 4, 4, 59, 59, 999, DateTimeKind.Utc).AddTicks(9999), saved.EndsAt);
    }

    [Fact]
    public async Task Cashier_CannotModifyTodaysPromotion_CreatedByAnotherUser()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        db.DailyPromotions.Add(Promotion(createdByUserId: 22));
        await db.SaveChangesAsync();
        var controller = Controller(db, userId: 11);

        var action = await controller.Upsert(7, ValidDto(minimumOrderValue: 50000), default);

        var forbidden = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Null(db.ChangeTracker.Entries<DailyPromotion>().Single().Property(x => x.MinimumOrderValue).OriginalValue);
        Assert.Null(db.DailyPromotions.Single().MinimumOrderValue);
    }

    [Fact]
    public async Task Cashier_CanModifyTodaysPromotion_WhenCreatedBySameUser()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        db.DailyPromotions.Add(Promotion(createdByUserId: 11));
        await db.SaveChangesAsync();
        var controller = Controller(db, userId: 11);

        var action = await controller.Upsert(7, ValidDto(minimumOrderValue: 50000), default);

        Assert.IsType<OkObjectResult>(action.Result);
        var saved = Assert.Single(db.DailyPromotions);
        Assert.Equal(50000, saved.MinimumOrderValue);
        Assert.Equal(11, saved.CreatedByUserId);
    }

    [Fact]
    public async Task Cashier_ScheduleIsNormalizedToTodaysFixedWindow()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        await db.SaveChangesAsync();
        var controller = Controller(db, userId: 11);
        var dto = ValidDto();
        dto.StartsAt = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
        dto.EndsAt = new DateTime(2026, 8, 5, 4, 59, 0, DateTimeKind.Utc);

        var action = await controller.Upsert(7, dto, default);

        Assert.IsType<OkObjectResult>(action.Result);
        var saved = Assert.Single(db.DailyPromotions);
        Assert.Equal(new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc), saved.StartsAt);
        Assert.Equal(new DateTime(2026, 8, 4, 4, 59, 59, 999, DateTimeKind.Utc).AddTicks(9999), saved.EndsAt);
    }

    [Fact]
    public async Task GetCurrent_MarksAnotherCashiersPromotionAsReadOnly()
    {
        await using var db = CreateDb();
        db.Branches.Add(Branch());
        db.DailyPromotions.Add(Promotion(createdByUserId: 22));
        await db.SaveChangesAsync();
        var controller = Controller(db, userId: 11);

        var action = await controller.GetCurrent(7, default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<DailyPromotionDto?>>(ok.Value);
        Assert.NotNull(response.Data);
        Assert.False(response.Data.CanManage);
        Assert.Equal(22, response.Data.CreatedByUserId);
    }

    private static DailyPromotion Promotion(int createdByUserId) => new()
    {
        BranchId = 7,
        CreatedByUserId = createdByUserId,
        Type = DailyPromotionType.FreeDelivery,
        IsActive = true,
        StartsAt = new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc),
        EndsAt = new DateTime(2026, 8, 4, 4, 59, 0, DateTimeKind.Utc),
        CreatedAt = NowUtc.AddMinutes(-30),
        UpdatedAt = NowUtc.AddMinutes(-30),
    };

    private static UpsertDailyPromotionDto ValidDto(int? minimumOrderValue = null) => new()
    {
        Type = nameof(DailyPromotionType.FreeDelivery),
        DiscountProductIds = [],
        MinimumOrderValue = minimumOrderValue,
        IsActive = true,
        StartsAt = new DateTime(2026, 8, 3, 10, 0, 0, DateTimeKind.Utc),
        EndsAt = new DateTime(2026, 8, 4, 4, 59, 0, DateTimeKind.Utc),
    };

    private static Branch Branch() => new()
    {
        Id = 7,
        Name = "Centro",
        Address = "Calle 1",
        Phone1 = "1",
    };

    private static DailyPromotionsController Controller(ApplicationDbContext db, int userId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Id).Returns(userId);
        currentUser.SetupGet(x => x.Role).Returns("cashier");
        currentUser.SetupGet(x => x.BranchId).Returns(7);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        return new DailyPromotionsController(db, currentUser.Object, new FakeClock(NowUtc));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
