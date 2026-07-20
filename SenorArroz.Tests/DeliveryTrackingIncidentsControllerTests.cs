using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Shared.Models;

namespace SenorArroz.Tests;

public class DeliveryTrackingIncidentsControllerTests
{
    private static readonly DateTime BaseTime =
        new(2026, 7, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAll_AdminOnlyReceivesOwnBranch()
    {
        await using var db = CreateDb();
        SeedNames(db);
        db.DeliveryTrackingIncidents.AddRange(
            Incident(1, branchId: 7, deliverymanId: 11),
            Incident(2, branchId: 8, deliverymanId: 12));
        await db.SaveChangesAsync();
        var controller = Controller(db, role: "admin", branchId: 7);

        var action = await controller.GetAll(cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ApiResponse<PagedResult<DeliveryTrackingIncidentListItemDto>>>(ok.Value);
        var item = Assert.Single(response.Data!.Items);
        Assert.Equal(1, item.Id);
        Assert.Equal("Domiciliario Uno", item.DeliverymanName);
        Assert.Equal("Centro", item.BranchName);
    }

    [Fact]
    public async Task Review_PreservesAutomaticFindingAndStoresHumanDecision()
    {
        await using var db = CreateDb();
        SeedNames(db);
        db.DeliveryTrackingIncidents.Add(Incident(1, branchId: 7, deliverymanId: 11));
        await db.SaveChangesAsync();
        var reviewedAt = BaseTime.AddHours(2);
        var controller = Controller(db, role: "admin", branchId: 7, userId: 21, now: reviewedAt);

        var action = await controller.Review(1, new ReviewDeliveryTrackingIncidentRequest
        {
            ReviewStatus = DeliveryIncidentReviewStatus.Justified,
            FinalClassification = DeliveryStayClassification.TrafficOrRoute,
            AdminNotes = "  Tráfico confirmado  ",
            DeliverymanExplanation = "  Había cierre vial  ",
        }, default);

        Assert.IsType<OkObjectResult>(action.Result);
        var saved = db.DeliveryTrackingIncidents.Single();
        Assert.Equal(DeliveryStayClassification.UnexpectedPlace, saved.StayClassification);
        Assert.Equal(DeliveryStayClassification.TrafficOrRoute, saved.FinalClassification);
        Assert.Equal(DeliveryIncidentReviewStatus.Justified, saved.ReviewStatus);
        Assert.Equal("Tráfico confirmado", saved.AdminNotes);
        Assert.Equal("Había cierre vial", saved.DeliverymanExplanation);
        Assert.Equal(21, saved.ReviewedByUserId);
        Assert.Equal(reviewedAt, saved.ReviewedAt);
    }

    private static DeliveryTrackingIncident Incident(long id, int branchId, int deliverymanId) => new()
    {
        Id = id,
        BranchId = branchId,
        DeliverymanId = deliverymanId,
        WorkSessionId = 30,
        DeliveryStayId = id + 100,
        IncidentType = DeliveryTrackingIncidentType.Stay,
        StayClassification = DeliveryStayClassification.UnexpectedPlace,
        StartedAt = BaseTime,
        EndedAt = BaseTime.AddMinutes(12),
        DurationSeconds = 720,
        CenterLatitude = 4.609710m,
        CenterLongitude = -74.081750m,
        RadiusMeters = 20,
        AverageAccuracyMeters = 8,
        SourceUpdatedAt = BaseTime.AddMinutes(12),
        EvidenceCapturedAt = BaseTime.AddMinutes(13),
        EvidenceComplete = true,
        CreatedAt = BaseTime.AddMinutes(13),
        UpdatedAt = BaseTime.AddMinutes(13),
    };

    private static void SeedNames(ApplicationDbContext db)
    {
        db.Branches.AddRange(
            new Branch { Id = 7, Name = "Centro", Address = "A", Phone1 = "1" },
            new Branch { Id = 8, Name = "Norte", Address = "B", Phone1 = "2" });
        db.Users.AddRange(
            new User { Id = 11, BranchId = 7, Name = "Domiciliario Uno", Email = "d1@test.co", Phone = "1", PasswordHash = "x", Role = UserRole.Deliveryman },
            new User { Id = 12, BranchId = 8, Name = "Domiciliario Dos", Email = "d2@test.co", Phone = "2", PasswordHash = "x", Role = UserRole.Deliveryman },
            new User { Id = 21, BranchId = 7, Name = "Administrador", Email = "a@test.co", Phone = "3", PasswordHash = "x", Role = UserRole.Admin });
    }

    private static DeliveryTrackingIncidentsController Controller(
        ApplicationDbContext db,
        string role,
        int branchId,
        int userId = 21,
        DateTime? now = null)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(x => x.Id).Returns(userId);
        current.SetupGet(x => x.Role).Returns(role);
        current.SetupGet(x => x.BranchId).Returns(branchId);
        current.SetupGet(x => x.IsAuthenticated).Returns(true);
        return new DeliveryTrackingIncidentsController(
            db,
            current.Object,
            new FakeClock(now ?? BaseTime.AddHours(1)));
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
