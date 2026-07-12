using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Services;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Tests;

public class WhatsAppAiOrchestratorTests
{
    [Theory]
    [InlineData(WhatsAppAttentionMode.Human)] [InlineData(WhatsAppAttentionMode.Paused)] [InlineData(WhatsAppAttentionMode.WaitingForHuman)] [InlineData(WhatsAppAttentionMode.Closed)]
    public async Task NonAiModes_AreIgnored(WhatsAppAttentionMode mode) { await using var f=await Fixture.Create(mode); var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1); Assert.True(r.Ignored); f.Provider.Verify(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()),Times.Never); }

    [Theory] [InlineData(false,true)] [InlineData(true,false)]
    public async Task InactiveOrUnverifiedAi_DoesNotCallProvider(bool active,bool verified){await using var f=await Fixture.Create(aiActive:active,verified:verified);var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.Ignored);f.Provider.Verify(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()),Times.Never);}

    [Fact] public async Task TextResponse_IsSentAndCompleted(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse("Hola",[],"model","stop",1,1));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.ResponseSent);Assert.Equal(WhatsAppAiProcessingStatus.Completed,(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingStatus);f.Sender.Verify(x=>x.SendTextAsync(1,1,It.IsAny<string>(),"Hola",It.IsAny<CancellationToken>()),Times.Once);}

    [Fact] public async Task AlreadyProcessed_IsNotClaimedTwice(){await using var f=await Fixture.Create();f.Claimer.Allow=false;var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.Ignored);f.Provider.Verify(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()),Times.Never);}

    [Fact] public async Task ValidTool_IsExecutedAndModelCalledAgain(){await using var f=await Fixture.Create();using var d=JsonDocument.Parse("{}");f.Provider.SetupSequence(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse(null,[new("c1","search_products",d.RootElement.Clone())],"model","tool_calls",null,null)).ReturnsAsync(new AiChatResponse("Resultado",[],"model","stop",null,null));f.Tools.Setup(x=>x.ExecuteAsync("search_products",It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AgentToolExecutionResult(true,new[]{"x"}));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.Equal(2,r.ModelCalls);Assert.Equal(1,r.ToolsExecuted);}

    [Fact] public async Task ToolLoopLimit_TransfersToHuman(){await using var f=await Fixture.Create(maxCalls:1);using var d=JsonDocument.Parse("{}");f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse(null,[new("c","search_products",d.RootElement.Clone())],"model",null,null,null));f.Tools.Setup(x=>x.ExecuteAsync(It.IsAny<string>(),It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AgentToolExecutionResult(true,new{}));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.TransferredToHuman);Assert.Equal(WhatsAppAttentionMode.WaitingForHuman,(await f.Db.WhatsAppConversations.FindAsync(1))!.AttentionMode);}

    [Fact] public async Task HumanChangeBeforeSend_PreventsResponse(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).Callback(()=>{f.Db.WhatsAppConversations.Find(1)!.AttentionMode=WhatsAppAttentionMode.Human;f.Db.SaveChanges();}).ReturnsAsync(new AiChatResponse("No enviar",[],"model",null,null,null));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.Ignored);f.Sender.Verify(x=>x.SendTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);}

    [Fact] public async Task TechnicalProviderError_RemainsPendingAndDoesNotTransfer(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse(null,[],"model",null,null,null,false,"invalid request"));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.False(r.TransferredToHuman);Assert.Equal(WhatsAppAttentionMode.Ai,(await f.Db.WhatsAppConversations.FindAsync(1))!.AttentionMode);Assert.Equal(WhatsAppAiProcessingStatus.Pending,(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingStatus);f.Sender.Verify(x=>x.SendTransferTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);}

    private sealed class Fixture:IAsyncDisposable
    {
      public Fixture(){Sender.Setup(x=>x.SendTransferTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppAutomaticSendResult(true,false,"transfer-wamid",null));}
      public ApplicationDbContext Db=null!;public Mock<IAiChatProvider> Provider=new();public Mock<IWhatsAppAutomaticMessageSender> Sender=new();public Mock<IAgentToolExecutor> Tools=new();public Mock<IWhatsAppSystemPromptBuilder> PromptBuilder=new();public FakeClaimer Claimer=null!;public IWhatsAppAiOrchestrator Orchestrator=null!;
      public static async Task<Fixture> Create(WhatsAppAttentionMode mode=WhatsAppAttentionMode.Ai,bool aiActive=true,bool verified=true,int maxCalls=4){var f=new Fixture();f.Db=new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);f.Db.WhatsAppConversations.Add(new WhatsAppConversation{Id=1,BranchId=1,PhoneNumber="1",AttentionMode=mode});f.Db.WhatsAppMessages.Add(new WhatsAppMessage{Id=1,ConversationId=1,Direction=WhatsAppMessageDirection.Inbound,Type=WhatsAppMessageType.Text,TextBody="Hola",Status=WhatsAppMessageStatus.Received,Timestamp=DateTime.UtcNow,AiProcessingStatus=WhatsAppAiProcessingStatus.Pending});f.Db.BranchAiSettings.Add(new BranchAiSetting{Id=1,BranchId=1,Provider="openai",Model="model",ApiKey="secret",IsActive=aiActive,IsVerified=verified});await f.Db.SaveChangesAsync();f.Claimer=new(f.Db);f.Provider.SetupGet(x=>x.ProviderName).Returns("openai");var resolver=new Mock<IAiChatProviderResolver>();resolver.Setup(x=>x.Resolve("openai")).Returns(f.Provider.Object);f.Sender.Setup(x=>x.SendTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppAutomaticSendResult(true,false,"wamid",null));f.Tools.SetupGet(x=>x.Definitions).Returns([]);f.PromptBuilder.Setup(x=>x.Build(It.IsAny<int>(),It.IsAny<CancellationToken>())).ReturnsAsync("prompt");var clock=new Mock<IClock>();clock.SetupGet(x=>x.UtcNow).Returns(DateTime.UtcNow);var notifications=new Mock<IWhatsAppNotificationService>();f.Orchestrator=new WhatsAppAiOrchestrator(f.Db,f.Claimer,resolver.Object,f.Tools.Object,f.Sender.Object,notifications.Object,f.PromptBuilder.Object,new WhatsAppAttentionService(),clock.Object,Options.Create(new WhatsAppAiOrchestratorOptions{MaxModelCallsPerMessage=maxCalls,TransientRetryCount=0}),NullLogger<WhatsAppAiOrchestrator>.Instance);return f;}
      public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
    private sealed class FakeClaimer(ApplicationDbContext db):IWhatsAppAiMessageClaimer{public bool Allow=true;public async Task<bool> TryClaimAsync(int c,int m,CancellationToken ct){if(!Allow)return false;Allow=false;var x=await db.WhatsAppMessages.FindAsync([m],ct);if(x?.AiProcessingStatus!=WhatsAppAiProcessingStatus.Pending)return false;x.AiProcessingStatus=WhatsAppAiProcessingStatus.Processing;x.AiProcessingAttempts++;await db.SaveChangesAsync(ct);return true;}}
}
