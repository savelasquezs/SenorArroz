using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.API.Controllers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Services;
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
            IsActive = true
        });
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.Role).Returns("admin");
        currentUser.SetupGet(x => x.BranchId).Returns(7);
        var modelResolver = new Mock<IAiProviderResolver>();
        modelResolver.Setup(x => x.ListModelsAsync("openai", "environment-secret", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiModelProviderResult(true, [new("chat-latest", "Chat Latest")], null));
        var apiKeys = new Mock<IAiApiKeyProvider>();
        apiKeys.Setup(x => x.GetApiKey("openai")).Returns("environment-secret");
        apiKeys.Setup(x => x.GetEnvironmentVariableName("openai")).Returns("OPENAI_API_KEY");
        var chatProvider = new Mock<IAiChatProvider>();
        AiChatRequest? captured = null;
        chatProvider.Setup(x => x.GenerateAsync(It.IsAny<AiChatRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiChatRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new AiChatResponse("OK", [], "chat-latest", "stop", 1, 1));
        var chatResolver = new Mock<IAiChatProviderResolver>();
        chatResolver.Setup(x => x.Resolve("openai")).Returns(chatProvider.Object);
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"}},"additionalProperties":false}""");
        var catalog = new Mock<IAgentToolCatalog>();
        catalog.SetupGet(x => x.All).Returns([new("search_products", "Busca productos", schema.RootElement.Clone())]);

        var controller = new BranchAiSettingsController(
            db,
            currentUser.Object,
            new FakeClock(DateTime.UtcNow),
            modelResolver.Object,
            chatResolver.Object,
            apiKeys.Object,
            NullLogger<BranchAiSettingsController>.Instance,
            Mock.Of<IWhatsAppSystemPromptBuilder>(),
            catalog.Object,
            new AiToolSchemaValidator());

        var action = await controller.TestConnection(7, default);

        Assert.IsType<OkObjectResult>(action.Result);
        var tool = Assert.Single(captured!.Tools);
        Assert.Equal("search_products", tool.Name);
        Assert.Equal("object", tool.ParametersSchema.GetProperty("type").GetString());
        Assert.Equal("environment-secret", captured.ApiKey);
        Assert.True((await db.BranchAiSettings.FindAsync(1))!.IsVerified);
    }
}
