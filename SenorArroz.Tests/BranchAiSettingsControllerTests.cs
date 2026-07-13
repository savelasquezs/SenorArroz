using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Domain.Entities;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class BranchAiSettingsControllerTests
{
    [Fact]
    public async Task TestConnection_ProbesChatEndpointWithAValidToolDefinition()
    {
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        db.BranchAiSettings.Add(new BranchAiSetting
        {
            Id = 1,
            BranchId = 7,
            Provider = "openai",
            Model = "chat-latest",
            ApiKey = "secret",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Role).Returns("admin");
        currentUser.SetupGet(x => x.BranchId).Returns(7);
        var modelResolver = new Mock<IAiProviderResolver>();
        modelResolver.Setup(x => x.ListModelsAsync("openai", "secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiModelProviderResult(true, [new("chat-latest", "Chat Latest")], null));
        var chatProvider = new Mock<IAiChatProvider>();
        AiChatRequest? captured = null;
        chatProvider.Setup(x => x.GenerateAsync(It.IsAny<AiChatRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiChatRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AiChatResponse("OK", [], "chat-latest", "stop", 1, 1));
        var chatResolver = new Mock<IAiChatProviderResolver>();
        chatResolver.Setup(x => x.Resolve("openai")).Returns(chatProvider.Object);

        var controller = new BranchAiSettingsController(
            db,
            currentUser.Object,
            new FakeClock(DateTime.UtcNow),
            modelResolver.Object,
            chatResolver.Object,
            NullLogger<BranchAiSettingsController>.Instance,
            Mock.Of<IWhatsAppSystemPromptBuilder>());

        var action = await controller.TestConnection(7, default);

        Assert.IsType<OkObjectResult>(action.Result);
        var tool = Assert.Single(captured!.Tools);
        Assert.Equal("compatibility_probe", tool.Name);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
        Assert.True((await db.BranchAiSettings.FindAsync(1))!.IsVerified);
    }
}
