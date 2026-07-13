using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Features.WhatsApp.DTOs;
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

    [Fact] public async Task Optimized_create_customer_refreshes_operational_state_for_second_request(){await using var f=await Fixture.Create(optimized:true);using var d=JsonDocument.Parse("""{"name":"Ana"}""");var requests=new List<AiChatRequest>();f.Provider.SetupSequence(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(()=>new AiChatResponse(null,[new("c1","create_customer",d.RootElement.Clone())],"model","tool_calls",null,null)).ReturnsAsync(()=>new AiChatResponse("Listo",[],"model","stop",null,null));f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).Callback<AiChatRequest,CancellationToken>((r,_)=>requests.Add(r)).ReturnsAsync(()=>requests.Count==1?new AiChatResponse(null,[new("c1","create_customer",d.RootElement.Clone())],"model","tool_calls",null,null):new AiChatResponse("Listo",[],"model","stop",null,null));f.Tools.Setup(x=>x.ExecuteAsync("create_customer",It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>())).Callback(()=>{var customer=new Customer{Id=7,Name="Ana",Phone1="1"};f.Db.Customers.Add(customer);f.Db.WhatsAppConversations.Find(1)!.CustomerId=7;f.Db.SaveChanges();}).ReturnsAsync(new AgentToolExecutionResult(true,new{}));await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.Equal(2,requests.Count);var state=requests[1].Messages.Single(x=>x.Role=="system"&&x.Content!.StartsWith(WhatsAppAiContextPlanner.RuntimeContextMarker));Assert.Contains("\"customerLinked\":true",state.Content);Assert.Contains(requests[1].Messages,x=>x.Role=="tool"&&x.ToolCallId=="c1");Assert.DoesNotContain(requests[1].Tools,x=>x.Name=="create_customer");Assert.Contains(requests[1].Tools,x=>x.Name=="get_or_create_order_draft");}

    [Fact] public async Task Optimized_add_item_replaces_old_state_and_keeps_tool_result(){await using var f=await Fixture.Create(optimized:true,withCustomer:true);f.Db.WhatsAppOrderDrafts.Add(new WhatsAppOrderDraft{Id=8,ConversationId=1,CustomerId=7,BranchId=1,Status=WhatsAppOrderDraftStatus.Building});await f.Db.SaveChangesAsync();using var d=JsonDocument.Parse("""{"productId":5,"quantity":1}""");var requests=new List<AiChatRequest>();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).Callback<AiChatRequest,CancellationToken>((r,_)=>requests.Add(r)).ReturnsAsync(()=>requests.Count==1?new AiChatResponse(null,[new("add1","add_draft_item",d.RootElement.Clone())],"model","tool_calls",null,null):new AiChatResponse("Listo",[],"model","stop",null,null));f.Tools.Setup(x=>x.ExecuteAsync("add_draft_item",It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>())).Callback(()=>{var product=new Product{Id=5,Name="Arroz especial",Active=true};f.Db.Products.Add(product);f.Db.WhatsAppOrderDrafts.Add(new WhatsAppOrderDraft{Id=9,ConversationId=1,CustomerId=7,BranchId=1,Status=WhatsAppOrderDraftStatus.Building});f.Db.SaveChanges();f.Db.WhatsAppOrderDraftItems.Add(new WhatsAppOrderDraftItem{Id=10,DraftId=9,ProductId=5,Quantity=1});f.Db.SaveChanges();}).ReturnsAsync(new AgentToolExecutionResult(true,new{}));await f.Orchestrator.ProcessIncomingMessageAsync(1,1);var second=requests[1];Assert.Single(second.Messages.Where(x=>x.Role=="system"&&x.Content!.StartsWith(WhatsAppAiContextPlanner.RuntimeContextMarker)));Assert.Contains("Arroz especial",second.Messages.Single(x=>x.Role=="system"&&x.Content!.StartsWith(WhatsAppAiContextPlanner.RuntimeContextMarker)).Content);Assert.Contains(second.Messages,x=>x.Role=="tool"&&x.ToolCallId=="add1");Assert.Contains(second.Tools,x=>x.Name=="set_payment_method");}

    [Fact] public async Task ToolLoopLimit_TransfersToHuman(){await using var f=await Fixture.Create(maxCalls:1);using var d=JsonDocument.Parse("{}");f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse(null,[new("c","search_products",d.RootElement.Clone())],"model",null,null,null));f.Tools.Setup(x=>x.ExecuteAsync(It.IsAny<string>(),It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AgentToolExecutionResult(true,new{}));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.TransferredToHuman);Assert.Equal(WhatsAppAttentionMode.WaitingForHuman,(await f.Db.WhatsAppConversations.FindAsync(1))!.AttentionMode);}

    [Fact] public async Task HumanChangeBeforeSend_PreventsResponse(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).Callback(()=>{f.Db.WhatsAppConversations.Find(1)!.AttentionMode=WhatsAppAttentionMode.Human;f.Db.SaveChanges();}).ReturnsAsync(new AiChatResponse("No enviar",[],"model",null,null,null));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.Ignored);f.Sender.Verify(x=>x.SendTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);}

    [Fact] public async Task TechnicalProviderError_RemainsPendingAndDoesNotTransfer(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse(null,[],"model",null,null,null,false,"invalid request"));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.False(r.TransferredToHuman);Assert.Equal(WhatsAppAttentionMode.Ai,(await f.Db.WhatsAppConversations.FindAsync(1))!.AttentionMode);Assert.Equal(WhatsAppAiProcessingStatus.Pending,(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingStatus);f.Sender.Verify(x=>x.SendTransferTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Never);}

    [Fact] public async Task RecoveredGeneratedResponse_IsSentWithoutCallingProvider(){await using var f=await Fixture.Create();var message=(await f.Db.WhatsAppMessages.FindAsync(1))!;message.AiGeneratedResponse="Respuesta ya generada";message.AiResponseAttemptId="attempt-1";await f.Db.SaveChangesAsync();var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.ResponseSent);f.Provider.Verify(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()),Times.Never);f.Sender.Verify(x=>x.SendTextAsync(1,1,"attempt-1","Respuesta ya generada",It.IsAny<CancellationToken>()),Times.Once);Assert.Equal(WhatsAppAiProcessingStatus.Completed,(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingStatus);}

    [Fact] public async Task TimeoutAfterMetaDispatchStarted_IsFailedWithoutRetryOrTransfer(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse("Hola",[],"model","stop",null,null));f.Sender.Setup(x=>x.SendTextAsync(1,1,It.IsAny<string>(),"Hola",It.IsAny<CancellationToken>())).Callback(()=>{var m=f.Db.WhatsAppMessages.Find(1)!;m.AiProcessingStatus=WhatsAppAiProcessingStatus.Sending;f.Db.SaveChanges();}).ThrowsAsync(new TaskCanceledException("timeout"));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.False(r.TransferredToHuman);Assert.Equal(WhatsAppAiProcessingStatus.Failed,(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingStatus);Assert.Contains("evitar duplicados",r.Error,StringComparison.OrdinalIgnoreCase);}

    [Fact] public async Task ExceptionAfterMetaSuccess_ReconcilesMessageAsCompleted(){await using var f=await Fixture.Create();f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiChatResponse("Hola",[],"model","stop",null,null));f.Sender.Setup(x=>x.SendTextAsync(1,1,It.IsAny<string>(),"Hola",It.IsAny<CancellationToken>())).Callback(()=>{var m=f.Db.WhatsAppMessages.Find(1)!;m.AiProcessingStatus=WhatsAppAiProcessingStatus.Sent;m.AiResponseWhatsAppMessageId="wamid";f.Db.SaveChanges();}).ThrowsAsync(new InvalidOperationException("notification failed"));var r=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);Assert.True(r.ResponseSent);Assert.Equal(WhatsAppAiProcessingStatus.Completed,(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingStatus);}

    [Fact]
    public async Task MetaFailureBetweenSendAndFinish_IsNotOverwrittenAsCompleted()
    {
      await using var f=await Fixture.Create();
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AiChatResponse("Hola",[],"model","stop",null,null));
      f.Sender.Setup(x=>x.SendTextAsync(1,1,It.IsAny<string>(),"Hola",It.IsAny<CancellationToken>()))
        .Callback(() =>
        {
          var message=f.Db.WhatsAppMessages.Find(1)!;
          message.AiProcessingStatus=WhatsAppAiProcessingStatus.Failed;
          message.AiProcessingError="Meta reportó que la respuesta de IA no pudo entregarse.";
          f.Db.SaveChanges();
        })
        .ReturnsAsync(new WhatsAppAutomaticSendResult(true,false,"wamid",null));

      await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      var stored=await f.Db.WhatsAppMessages.FindAsync(1);
      Assert.Equal(WhatsAppAiProcessingStatus.Failed,stored!.AiProcessingStatus);
      Assert.Contains("Meta reportó",stored.AiProcessingError);
    }

    [Fact]
    public async Task TransferNoticeFailure_IsVisibleInDiagnostics()
    {
      await using var f=await Fixture.Create(maxCalls:1);
      using var arguments=JsonDocument.Parse("{}");
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AiChatResponse(null,[new("call-1","search_products",arguments.RootElement.Clone())],"model","tool_calls",null,null));
      f.Tools.Setup(x=>x.ExecuteAsync(It.IsAny<string>(),It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AgentToolExecutionResult(true,new{}));
      f.Sender.Setup(x=>x.SendTransferTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new WhatsAppAutomaticSendResult(false,false,null,"Meta WhatsApp HTTP 400: destinatario inválido"));

      var result=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      var stored=await f.Db.WhatsAppMessages.FindAsync(1);
      Assert.True(result.TransferredToHuman);
      Assert.Equal(WhatsAppAiProcessingStatus.TransferredToHuman,stored!.AiProcessingStatus);
      Assert.Contains("Aviso al cliente no entregado",stored.AiProcessingError);
      Assert.Contains("HTTP 400",stored.AiProcessingError);
      f.Notifications.Verify(x=>x.NotifyAiProcessingChangedAsync(
        1,
        It.Is<WhatsAppAiProcessingDto>(d=>d.Status=="transferredToHuman"&&d.TechnicalDetail!.Contains("Aviso al cliente no entregado")),
        It.IsAny<CancellationToken>()),Times.AtLeastOnce);
    }

    [Fact]
    public async Task AttentionRealtimeFailure_DoesNotPreventTransferNotice()
    {
      await using var f=await Fixture.Create(maxCalls:1);
      using var arguments=JsonDocument.Parse("{}");
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AiChatResponse(null,[new("call-1","search_products",arguments.RootElement.Clone())],"model","tool_calls",null,null));
      f.Tools.Setup(x=>x.ExecuteAsync(It.IsAny<string>(),It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AgentToolExecutionResult(true,new{}));
      f.Notifications.Setup(x=>x.NotifyAttentionChangedAsync(
          It.IsAny<int>(),It.IsAny<WhatsAppConversationDto>(),It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("SignalR unavailable"));

      var result=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      Assert.True(result.TransferredToHuman);
      f.Sender.Verify(x=>x.SendTransferTextAsync(
        1,1,It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>()),Times.Once);
    }

    [Fact]
    public async Task ProviderQuotaError_EmitsRetryDiagnosticWithHttpStatus()
    {
      await using var f=await Fixture.Create();
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AiChatResponse(null,[],"model",null,null,null,true,"RESOURCE_EXHAUSTED: quota exceeded",429));

      await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      f.Notifications.Verify(x=>x.NotifyAiProcessingChangedAsync(
        1,
        It.Is<WhatsAppAiProcessingDto>(d=>d.Status=="pending"&&d.ErrorCategory=="quota"&&d.HttpStatusCode==429&&d.WillRetry),
        It.IsAny<CancellationToken>()),Times.Once);
      Assert.Contains("HTTP 429",(await f.Db.WhatsAppMessages.FindAsync(1))!.AiProcessingError);
    }

    [Fact]
    public async Task HumanTransferTool_PreservesTransferredStatusAndReason()
    {
      await using var f=await Fixture.Create();
      using var arguments=JsonDocument.Parse("{}");
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .ReturnsAsync(new AiChatResponse(null,[new("call-1","request_human_assistance",arguments.RootElement.Clone())],"model","tool_calls",null,null));
      f.Tools.Setup(x=>x.ExecuteAsync("request_human_assistance",It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>()))
        .Callback(()=>
        {
          f.Db.WhatsAppConversations.Find(1)!.AttentionMode=WhatsAppAttentionMode.WaitingForHuman;
          var message=f.Db.WhatsAppMessages.Find(1)!;
          message.AiProcessingStatus=WhatsAppAiProcessingStatus.TransferredToHuman;
          message.AiProcessingError="El cliente pidió hablar con un asesor.";
          f.Db.SaveChanges();
        })
        .ReturnsAsync(new AgentToolExecutionResult(true,new{transferred=true},Code:"human_required",Message:"Transferida.",TransferredToHuman:true));

      var result=await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      var stored=await f.Db.WhatsAppMessages.FindAsync(1);
      Assert.True(result.TransferredToHuman);
      Assert.Equal(WhatsAppAiProcessingStatus.TransferredToHuman,stored!.AiProcessingStatus);
      Assert.Equal("El cliente pidió hablar con un asesor.",stored.AiProcessingError);
      f.Provider.Verify(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()),Times.Once);
    }

    [Fact]
    public async Task Optimized_unexposed_tool_is_not_executed_and_returns_controlled_result()
    {
      await using var f=await Fixture.Create(optimized:true);
      using var arguments=JsonDocument.Parse("{}");
      var requests=new List<AiChatRequest>();
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .Callback<AiChatRequest,CancellationToken>((request,_)=>requests.Add(request))
        .ReturnsAsync(()=>requests.Count==1
          ? new AiChatResponse(null,[new("hidden-1","set_payment_method",arguments.RootElement.Clone())],"model","tool_calls",null,null)
          : new AiChatResponse("Continuar",[],"model","stop",null,null));

      await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      f.Tools.Verify(x=>x.ExecuteAsync("set_payment_method",It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>()),Times.Never);
      Assert.Contains(requests[1].Messages,x=>x.Role=="tool"&&x.ToolCallId=="hidden-1"&&x.Content!.Contains("tool_not_exposed"));
      Assert.Empty(f.Db.WhatsAppOrderDrafts);
    }

    [Fact]
    public async Task Optimized_payment_mutation_is_visible_to_the_next_request()
    {
      await using var f=await Fixture.Create(optimized:true,withCustomer:true);
      var product=new Product{Id=5,Name="Arroz",Active=true};f.Db.Products.Add(product);
      f.Db.WhatsAppOrderDrafts.Add(new WhatsAppOrderDraft{Id=8,ConversationId=1,CustomerId=7,BranchId=1,Status=WhatsAppOrderDraftStatus.AwaitingPayment,OrderType=OrderType.Onsite,Items={new WhatsAppOrderDraftItem{Id=10,ProductId=5,Product=product,Quantity=1}}});
      await f.Db.SaveChangesAsync();
      using var arguments=JsonDocument.Parse("""{"paymentMethodId":0}""");
      var requests=new List<AiChatRequest>();
      f.Provider.Setup(x=>x.GenerateAsync(It.IsAny<AiChatRequest>(),It.IsAny<CancellationToken>()))
        .Callback<AiChatRequest,CancellationToken>((request,_)=>requests.Add(request))
        .ReturnsAsync(()=>requests.Count==1
          ? new AiChatResponse(null,[new("pay-1","set_payment_method",arguments.RootElement.Clone())],"model","tool_calls",null,null)
          : new AiChatResponse("Resumen",[],"model","stop",null,null));
      f.Tools.Setup(x=>x.ExecuteAsync("set_payment_method",It.IsAny<AgentToolExecutionContext>(),It.IsAny<JsonElement>(),It.IsAny<CancellationToken>()))
        .Callback(()=>{var draft=f.Db.WhatsAppOrderDrafts.Find(8)!;draft.PaymentMethod="cash";draft.Status=WhatsAppOrderDraftStatus.ReadyForConfirmation;f.Db.SaveChanges();})
        .ReturnsAsync(new AgentToolExecutionResult(true,new{}));

      await f.Orchestrator.ProcessIncomingMessageAsync(1,1);

      var state=requests[1].Messages.Single(x=>x.Role=="system"&&x.Content!.StartsWith(WhatsAppAiContextPlanner.RuntimeContextMarker));
      Assert.Contains("\"paymentMethod\":\"cash\"",state.Content);
      Assert.Contains(requests[1].Tools,x=>x.Name=="get_order_confirmation_summary");
      Assert.Contains(requests[1].Messages,x=>x.Role=="tool"&&x.ToolCallId=="pay-1");
    }

    private sealed class Fixture:IAsyncDisposable
    {
      public Fixture(){Sender.Setup(x=>x.SendTransferTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppAutomaticSendResult(true,false,"transfer-wamid",null));}
      public ApplicationDbContext Db=null!;public Mock<IAiChatProvider> Provider=new();public Mock<IWhatsAppAutomaticMessageSender> Sender=new();public Mock<IAgentToolExecutor> Tools=new();public Mock<IWhatsAppSystemPromptBuilder> PromptBuilder=new();public Mock<IWhatsAppNotificationService> Notifications=new();public FakeClaimer Claimer=null!;public IWhatsAppAiOrchestrator Orchestrator=null!;
      public static async Task<Fixture> Create(WhatsAppAttentionMode mode=WhatsAppAttentionMode.Ai,bool aiActive=true,bool verified=true,int maxCalls=4,bool optimized=false,bool withCustomer=false){var f=new Fixture();f.Db=new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);f.Db.Branches.Add(new Branch{Id=1,Name="Centro"});f.Db.WhatsAppConversations.Add(new WhatsAppConversation{Id=1,BranchId=1,PhoneNumber="1",AttentionMode=mode,CustomerId=withCustomer?7:null});if(withCustomer)f.Db.Customers.Add(new Customer{Id=7,Name="Ana",Phone1="1"});f.Db.WhatsAppMessages.Add(new WhatsAppMessage{Id=1,ConversationId=1,Direction=WhatsAppMessageDirection.Inbound,Type=WhatsAppMessageType.Text,TextBody="Hola",Status=WhatsAppMessageStatus.Received,Timestamp=DateTime.UtcNow,AiProcessingStatus=WhatsAppAiProcessingStatus.Pending});f.Db.BranchAiSettings.Add(new BranchAiSetting{Id=1,BranchId=1,Provider="openai",Model="model",ApiKey="secret",IsActive=aiActive,IsVerified=verified,ContextStrategy=optimized?"optimized_v1":"legacy"});await f.Db.SaveChangesAsync();f.Claimer=new(f.Db);f.Provider.SetupGet(x=>x.ProviderName).Returns("openai");var resolver=new Mock<IAiChatProviderResolver>();resolver.Setup(x=>x.Resolve("openai")).Returns(f.Provider.Object);f.Sender.Setup(x=>x.SendTextAsync(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<string>(),It.IsAny<string>(),It.IsAny<CancellationToken>())).ReturnsAsync(new WhatsAppAutomaticSendResult(true,false,"wamid",null));f.Tools.SetupGet(x=>x.Definitions).Returns([]);f.PromptBuilder.Setup(x=>x.Build(It.IsAny<int>(),It.IsAny<CancellationToken>())).ReturnsAsync("prompt");var clock=new Mock<IClock>();clock.SetupGet(x=>x.UtcNow).Returns(DateTime.UtcNow);IWhatsAppAiContextPlanner? planner=null;IAgentToolCatalog? catalog=null;if(optimized){using var schema=JsonDocument.Parse("""{"type":"object"}""");var names=new[]{"request_human_assistance","search_products","get_product_details","send_menu","send_product_details","find_customer_by_phone","create_customer","update_customer","list_customer_addresses","get_or_create_order_draft","get_order_draft","add_draft_item","remove_draft_item","set_draft_notes","set_order_type","cancel_order_draft","select_customer_address","create_customer_address","save_validated_customer_address","find_registered_neighborhood","resolve_address_with_maps","validate_delivery_address","calculate_delivery_fee","list_payment_methods","set_payment_method","calculate_cash_change","get_order_confirmation_summary","mark_draft_ready_for_confirmation"};var definitions=names.Select(x=>new AiToolDefinition(x,x,schema.RootElement.Clone())).ToList();var catalogMock=new Mock<IAgentToolCatalog>();catalogMock.SetupGet(x=>x.All).Returns(definitions);catalogMock.Setup(x=>x.GetByNames(It.IsAny<IEnumerable<string>>())).Returns<IEnumerable<string>>(selected=>definitions.Where(x=>selected.Contains(x.Name,StringComparer.OrdinalIgnoreCase)).ToList());catalogMock.Setup(x=>x.ModifiesData(It.IsAny<string>())).Returns<string>(name=>name is "create_customer" or "add_draft_item" or "set_payment_method");catalog=catalogMock.Object;planner=new WhatsAppAiContextPlanner(Options.Create(new WhatsAppAiContextOptimizationOptions()),NullLogger<WhatsAppAiContextPlanner>.Instance);}f.Orchestrator=new WhatsAppAiOrchestrator(f.Db,f.Claimer,resolver.Object,f.Tools.Object,f.Sender.Object,f.Notifications.Object,f.PromptBuilder.Object,new WhatsAppAttentionService(),clock.Object,Options.Create(new WhatsAppAiOrchestratorOptions{MaxModelCallsPerMessage=maxCalls,TransientRetryCount=0}),NullLogger<WhatsAppAiOrchestrator>.Instance,contextPlanner:planner,toolCatalog:catalog);return f;}
      public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
    private sealed class FakeClaimer(ApplicationDbContext db):IWhatsAppAiMessageClaimer
    {
      public bool Allow=true;
      public async Task<bool> TryClaimAsync(int c,int m,CancellationToken ct){if(!Allow)return false;Allow=false;var x=await db.WhatsAppMessages.FindAsync([m],ct);if(x?.AiProcessingStatus!=WhatsAppAiProcessingStatus.Pending)return false;x.AiProcessingStatus=WhatsAppAiProcessingStatus.Processing;x.AiProcessingAttempts++;await db.SaveChangesAsync(ct);return true;}
      public async Task<bool> TryCompleteAsync(int c,int m,DateTime at,CancellationToken ct){var x=await db.WhatsAppMessages.FindAsync([m],ct);if(x is null||x.ConversationId!=c||x.AiProcessingStatus is WhatsAppAiProcessingStatus.Failed or WhatsAppAiProcessingStatus.TransferredToHuman)return false;x.AiProcessingStatus=WhatsAppAiProcessingStatus.Completed;x.AiProcessedAt=at;x.AiProcessingError=null;x.AiProcessingStartedAt=null;x.AiNextRetryAt=null;await db.SaveChangesAsync(ct);return true;}
    }
}
