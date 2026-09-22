using AutoMapper;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.API.Services;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.Commands;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Application.Features.BranchPrintSettings.Queries;
using SenorArroz.Application.Mappings;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class BranchPrintSettingsTests
{
    [Theory]
    [InlineData(BranchPrintSettings.KitchenAutoPrintWhenOrderCreated)]
    [InlineData(BranchPrintSettings.KitchenAutoPrintWhenMarkedReady)]
    public async Task Update_handler_persists_and_returns_kitchen_trigger(string trigger)
    {
        await using var db = CreateDb();
        db.BranchPrintSettings.Add(new BranchPrintSettings { BranchId = 2 });
        await db.SaveChangesAsync();
        var handler = new UpdateBranchPrintSettingsHandler(db, CreateMapper());

        var result = await handler.Handle(
            new UpdateBranchPrintSettingsCommand(
                2,
                new UpdateBranchPrintSettingsDto { KitchenAutoPrintTrigger = trigger }),
            CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(trigger, result.KitchenAutoPrintTrigger);
        Assert.Equal(
            trigger,
            await db.BranchPrintSettings
                .Where(x => x.BranchId == 2)
                .Select(x => x.KitchenAutoPrintTrigger)
                .SingleAsync());
    }

    [Fact]
    public async Task Update_returns_success_when_config_notification_fails_after_save()
    {
        var mediator = new Mock<IMediator>();
        var saved = new BranchPrintSettingsDto
        {
            KitchenAutoPrintTrigger = BranchPrintSettings.KitchenAutoPrintWhenOrderCreated,
        };
        mediator.Setup(x => x.Send(It.IsAny<UpdateBranchPrintSettingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);
        mediator.Setup(x => x.Send(It.IsAny<GetPrintAgentConfigQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PrintAgentConfigDto());

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Role).Returns("superadmin");
        var notifier = new Mock<IPrintAgentNotificationService>();
        notifier.Setup(x => x.NotifyConfigChangedAsync(2, It.IsAny<PrintAgentConfigDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));
        var controller = new BranchPrintSettingsController(
            mediator.Object,
            currentUser.Object,
            notifier.Object,
            NullLogger<BranchPrintSettingsController>.Instance);

        var response = await controller.Update(
            2,
            new UpdateBranchPrintSettingsDto
            {
                KitchenAutoPrintTrigger = BranchPrintSettings.KitchenAutoPrintWhenOrderCreated,
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(response.Result);
    }

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options, currentTenant: TestTenantContext.Default, tenantExecutionContext: TestTenantContext.Default);
    }

    private static IMapper CreateMapper()
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<BranchMappingProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }
}
