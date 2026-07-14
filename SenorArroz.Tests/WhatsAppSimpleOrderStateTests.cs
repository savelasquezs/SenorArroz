using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class WhatsAppSimpleOrderStateTests
{
    [Fact]
    public async Task AddProduct_PersistsJsonCartWithServerPrice()
    {
        await using var db=Db();Seed(db,1,1,12000,10);await db.SaveChangesAsync();
        var service=Service(db);var tool=new ApplyOrderActionAgentTool(service,db);
        using var args=JsonDocument.Parse("""{"action":"add_product","productId":1,"quantity":2}""");
        var result=await tool.ExecuteAsync(new(1,1,ExecutionId:"run"),args.RootElement,default);
        Assert.True(result.Success);var state=await service.LoadAsync(1);Assert.Equal(2,state.Items.Single().Quantity);Assert.Contains(state.Activities,x=>x.Message.Contains("Paisa"));
        Assert.Equal(24000,(await service.BuildSummaryAsync(1,state)).Subtotal);
    }

    [Fact]
    public async Task RepeatedOperation_IsIdempotent()
    {
        await using var db=Db();Seed(db,1,1,12000,10);await db.SaveChangesAsync();var service=Service(db);var tool=new ApplyOrderActionAgentTool(service,db);
        using var args=JsonDocument.Parse("""{"action":"add_product","productId":1,"quantity":1}""");var context=new SenorArroz.Application.Common.Models.AgentToolExecutionContext(1,1,ExecutionId:"same");
        await tool.ExecuteAsync(context,args.RootElement,default);await tool.ExecuteAsync(context,args.RootElement,default);
        Assert.Equal(1,(await service.LoadAsync(1)).Items.Single().Quantity);
    }

    [Fact]
    public async Task ProductFromOtherBranch_IsRejected()
    {
        await using var db=Db();Seed(db,1,2,12000,10);await db.SaveChangesAsync();var tool=new ApplyOrderActionAgentTool(Service(db),db);
        using var args=JsonDocument.Parse("""{"action":"add_product","productId":1}""");var result=await tool.ExecuteAsync(new(1,1),args.RootElement,default);
        Assert.False(result.Success);Assert.Equal("product_not_found",result.Code);
    }

    [Fact]
    public async Task AddAgainAccumulates_ThenSetRemoveAndClearWork()
    {
        await using var db=Db();Seed(db,1,1,12000,10);await db.SaveChangesAsync();var service=Service(db);var tool=new ApplyOrderActionAgentTool(service,db);
        async Task Run(string json,string execution){using var doc=JsonDocument.Parse(json);Assert.True((await tool.ExecuteAsync(new(1,1,ExecutionId:execution),doc.RootElement,default)).Success);}
        await Run("""{"action":"add_product","productId":1,"quantity":2}""","add1");await Run("""{"action":"add_product","productId":1,"quantity":3}""","add2");Assert.Equal(5,(await service.LoadAsync(1)).Items.Single().Quantity);
        await Run("""{"action":"set_quantity","productId":1,"quantity":4}""","set");Assert.Equal(4,(await service.LoadAsync(1)).Items.Single().Quantity);
        await Run("""{"action":"remove_product","productId":1}""","remove");Assert.Empty((await service.LoadAsync(1)).Items);
        await Run("""{"action":"add_product","productId":1}""","add3");await Run("""{"action":"clear_cart"}""","clear");Assert.Empty((await service.LoadAsync(1)).Items);
    }

    [Fact]
    public async Task InactiveAndInsufficientStock_AreRejected()
    {
        await using var db=Db();Seed(db,1,1,12000,1);db.Products.Local.Single().Active=false;await db.SaveChangesAsync();var tool=new ApplyOrderActionAgentTool(Service(db),db);
        using(var inactive=JsonDocument.Parse("""{"action":"add_product","productId":1}""")){var result=await tool.ExecuteAsync(new(1,1),inactive.RootElement,default);Assert.Equal("product_unavailable",result.Code);}
        db.Products.Local.Single().Active=true;await db.SaveChangesAsync();using var stock=JsonDocument.Parse("""{"action":"add_product","productId":1,"quantity":2}""");var rejected=await tool.ExecuteAsync(new(1,1),stock.RootElement,default);Assert.Equal("insufficient_stock",rejected.Code);
    }

    [Fact]
    public async Task StateSurvivesServiceRecreation()
    {
        await using var db=Db();Seed(db,1,1,12000,10);await db.SaveChangesAsync();var first=Service(db);await first.SaveAsync(1,new(){Items=[new(){ProductId=1,Quantity=2}]});var second=Service(db);Assert.Equal(2,(await second.LoadAsync(1)).Items.Single().Quantity);
    }

    [Fact]
    public async Task ExpiredState_IsCleared()
    {
        await using var db=Db();var now=new DateTime(2026,7,13,12,0,0,DateTimeKind.Utc);var clock=Mock.Of<IClock>(x=>x.UtcNow==now);
        db.WhatsAppConversations.Add(new(){Id=1,BranchId=1,PhoneNumber="300",AttentionMode=WhatsAppAttentionMode.Ai,AiOrderState="{\"version\":1,\"items\":[{\"productId\":1,\"quantity\":1}]}",AiOrderStateUpdatedAt=now.AddMinutes(-61)});await db.SaveChangesAsync();
        var state=await new WhatsAppSimpleOrderStateService(db,clock).LoadAsync(1);Assert.Empty(state.Items);Assert.Null((await db.WhatsAppConversations.FindAsync(1))!.AiOrderState);
    }

    [Fact]
    public void FinalFourToolSchemas_AreProviderCompatible()
    {
        using var db=Db();var state=Service(db);var sender=Mock.Of<IWhatsAppAutomaticMessageSender>();
        IAgentTool[] tools=[new ApplyOrderActionAgentTool(state,db),new SendMenuAgentTool(db,sender),new SendProductDetailsAgentTool(db,sender),new RequestHumanAssistanceAgentTool(db,new SenorArroz.Domain.Services.WhatsAppAttentionService(),Mock.Of<IWhatsAppNotificationService>(),sender,Mock.Of<IClock>(),NullLogger<RequestHumanAssistanceAgentTool>.Instance)];
        Assert.Equal(["apply_order_action","send_menu","send_product_details","request_human_assistance"],tools.Select(x=>x.Name));
        new SenorArroz.Application.Common.Services.AiToolSchemaValidator().ValidateOrThrow(tools.Select(x=>new SenorArroz.Application.Common.Models.AiToolDefinition(x.Name,x.Description,x.ParametersSchema)).ToList());
        Assert.All(tools,x=>Assert.DoesNotContain(x.ParametersSchema.EnumerateObject(),p=>p.Name is "oneOf" or "anyOf" or "allOf"));
    }

    private static WhatsAppSimpleOrderStateService Service(ApplicationDbContext db)=>new(db,Mock.Of<IClock>(x=>x.UtcNow==new DateTime(2026,7,13,12,0,0,DateTimeKind.Utc)));
    private static void Seed(ApplicationDbContext db,int productId,int productBranch,int price,int? stock)
    {
        db.Branches.AddRange(new Branch{Id=1,Name="Uno"},new Branch{Id=2,Name="Dos"});var category=new ProductCategory{Id=productBranch,BranchId=productBranch,Name="Arroces"};db.ProductCategories.Add(category);db.Products.Add(new Product{Id=productId,CategoryId=category.Id,Name="Paisa Dúo",Price=price,Stock=stock,Active=true});db.WhatsAppConversations.Add(new(){Id=1,BranchId=1,PhoneNumber="300",AttentionMode=WhatsAppAttentionMode.Ai});
    }
    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
