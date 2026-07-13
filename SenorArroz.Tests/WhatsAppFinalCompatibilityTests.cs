using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Infrastructure.Data;
using SenorArroz.Infrastructure.Services;

namespace SenorArroz.Tests;

public class WhatsAppFinalCompatibilityTests
{
    [Fact]
    public void Search_products_has_provider_compatible_root_and_validates_arguments_internally()
    {
        var tool=new SearchProductsAgentTool(Mock.Of<IWhatsAppProductMatcher>());
        var schema=tool.ParametersSchema;
        Assert.Equal("object",schema.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object,schema.GetProperty("properties").ValueKind);
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.False(schema.TryGetProperty("required",out _));
        foreach(var keyword in new[]{"anyOf","oneOf","allOf","not","const","enum"})Assert.False(schema.TryGetProperty(keyword,out _));
        Assert.Null(new AiToolSchemaValidator().Validate([new(tool.Name,tool.Description,schema)]));
    }

    [Fact]
    public void Schema_validator_reports_tool_property_and_location()
    {
        using var schema=JsonDocument.Parse("""{"type":"object","properties":{},"anyOf":[],"additionalProperties":false}""");
        var error=new AiToolSchemaValidator().Validate([new("search_products","Busca",schema.RootElement.Clone())]);
        Assert.NotNull(error);Assert.Equal("search_products",error.ToolName);Assert.Equal("$.anyOf",error.Location);Assert.Contains("anyOf",error.Message);
    }

    [Fact]
    public async Task People_filter_is_strict_before_scoring_and_reports_no_range()
    {
        await using var db=Db();var branch=new Branch{Id=1,Name="Centro"};var category=new ProductCategory{Id=1,BranchId=1,Branch=branch,Name="Arroces"};
        db.AddRange(branch,category,
            new Product{Id=1,Category=category,Name="Arroz Paisa Dúo",Active=true,ServesPeopleMin=2,ServesPeopleMax=2},
            new Product{Id=2,Category=category,Name="Arroz Ranchero Dúo",Active=true,ServesPeopleMin=2,ServesPeopleMax=3},
            new Product{Id=3,Category=category,Name="Arroz Ropa Vieja Súper",Active=true,ServesPeopleMin=2,ServesPeopleMax=4},
            new Product{Id=4,Category=category,Name="Arroz Familiar",Active=true,ServesPeopleMin=7,ServesPeopleMax=9},
            new Product{Id=5,Category=category,Name="Arroz Paisa Trío",Active=true,ServesPeopleMin=3,ServesPeopleMax=3});
        await db.SaveChangesAsync();var matcher=new WhatsAppProductMatcher(db);
        var two=await matcher.MatchAsync(1,"Hola, dame un arroz para 2 por favor",2);
        Assert.Equal(3,two.Products.Count);Assert.DoesNotContain(two.Products,x=>x.ProductId==4);Assert.All(two.Products,x=>Assert.True(x.ServesPeopleMin<=2&&x.ServesPeopleMax>=2));
        var trio=await matcher.MatchAsync(1,"Hola, dame un paisa trio por favor",3);
        Assert.Equal("paisa trio",trio.NormalizedQuery);Assert.Equal(5,trio.Products[0].ProductId);
        var none=await matcher.MatchAsync(1,null,12);
        Assert.Empty(none.Products);Assert.True(none.ServesPeopleUnavailable);Assert.False(none.NeedsClarification);
    }

    [Fact]
    public async Task Draft_session_uses_newest_recent_and_expires_old_and_duplicate()
    {
        await using var db=Db();var now=new DateTime(2026,7,13,12,0,0,DateTimeKind.Utc);
        var old=new WhatsAppOrderDraft{Id=1,ConversationId=1,BranchId=1,Status=WhatsAppOrderDraftStatus.Building,UpdatedAt=now.AddHours(-2)};
        var recent=new WhatsAppOrderDraft{Id=2,ConversationId=1,BranchId=1,Status=WhatsAppOrderDraftStatus.AwaitingPayment,UpdatedAt=now.AddMinutes(-20)};
        var newest=new WhatsAppOrderDraft{Id=3,ConversationId=1,BranchId=1,Status=WhatsAppOrderDraftStatus.Building,UpdatedAt=now.AddMinutes(-5)};
        db.AddRange(old,recent,newest);await db.SaveChangesAsync();
        var clock=Mock.Of<IClock>(x=>x.UtcNow==now);var session=new WhatsAppOrderDraftSession(db,Options.Create(new WhatsAppOrderDraftOptions{ResumeWindowMinutes=60}),clock);
        Assert.Equal(3,(await session.LoadActiveAsync(1))!.Id);Assert.Equal(WhatsAppOrderDraftStatus.Expired,old.Status);Assert.Equal(WhatsAppOrderDraftStatus.Expired,recent.Status);
        Assert.Null(await session.LoadActiveAsync(99));
    }

    [Fact]
    public async Task Multiple_valid_addresses_use_complete_body_short_titles_and_stable_ids()
    {
        await using var db=Db();var branch=new Branch{Id=1,Name="Centro"};var neighborhood=new Neighborhood{Id=1,BranchId=1,Branch=branch,Name="Amazonia",Active=true};var customer=new Customer{Id=7,BranchId=1,Name="Ana",Phone1="1"};
        db.AddRange(branch,neighborhood,customer,new Address{Id=123,CustomerId=7,Neighborhood=neighborhood,AddressText="Cr 59 #27B-387",AdditionalInfo="Torre 3 apto 709",IsPrimary=true},new Address{Id=124,CustomerId=7,Neighborhood=neighborhood,AddressText="Calle 10 #20-30",AdditionalInfo="Casa azul"});await db.SaveChangesAsync();
        string? body=null;IReadOnlyList<WhatsAppReplyButton>? buttons=null;var sender=new Mock<IWhatsAppAutomaticMessageSender>();
        sender.Setup(x=>x.SendAgentReplyButtonsAsync(1,"run:addresses",It.IsAny<string>(),It.IsAny<IReadOnlyList<WhatsAppReplyButton>>(),It.IsAny<CancellationToken>())).Callback<int,string,string,IReadOnlyList<WhatsAppReplyButton>,CancellationToken>((_,_,text,value,_)=>{body=text;buttons=value;}).ReturnsAsync(new WhatsAppAutomaticSendResult(true,false,"wamid",null));
        using var args=JsonDocument.Parse("{}");var result=await new ListCustomerAddressesAgentTool(db,sender.Object).ExecuteAsync(new(1,1,CustomerId:7,ExecutionId:"run"),args.RootElement,default);
        Assert.Contains("Cr 59 #27B-387, Torre 3 apto 709, Amazonia",body);Assert.Contains("Calle 10 #20-30, Casa azul, Amazonia",body);
        Assert.Equal(["address:123","address:124"],buttons!.Select(x=>x.Id));Assert.Equal(["Dirección 1","Dirección 2"],buttons!.Select(x=>x.Title));Assert.All(buttons!,x=>Assert.True(x.Title.Length<=20));Assert.Contains("\"selectionButtonsSent\":true",JsonSerializer.Serialize(result.Data));
    }

    [Fact]
    public void Tool_codes_drive_response_without_substring_heuristics()
    {
        var menu=JsonSerializer.Serialize(new AgentToolExecutionResult(true,new{},Code:"menu_sent"));
        Assert.Equal("¿Cuál deseas pedir?",WhatsAppConversationPolicy.EnforceToolAwareResponse("Texto largo",menu));
        var fake=JsonSerializer.Serialize(new AgentToolExecutionResult(true,new{note="products_found"},Code:"ok"));
        Assert.Equal("Respuesta normal",WhatsAppConversationPolicy.EnforceToolAwareResponse("Respuesta normal.",fake));
        var products=JsonSerializer.Serialize(new AgentToolExecutionResult(true,new{Products=new[]{new{Name="A"},new{Name="B"},new{Name="C"},new{Name="D"}}},Code:"products_found"));
        var formatted=WhatsAppConversationPolicy.EnforceToolAwareResponse("publicidad",products);Assert.DoesNotContain("D",formatted);Assert.True(formatted.Length<=300);Assert.Equal(1,formatted.Count(x=>x=='?'));
        var confirmation=JsonSerializer.Serialize(new AgentToolExecutionResult(true,new{summary=new{items=new[]{new{name="Paisa Dúo",Quantity=1,Subtotal=73000}},DeliveryFee=5000,DiscountTotal=0,Total=78000}},Code:"confirmation_required"));
        var summary=WhatsAppConversationPolicy.EnforceToolAwareResponse("ocultar",confirmation);Assert.Contains("1 x Paisa Dúo: $73.000",summary);Assert.Contains("Domicilio: $5.000",summary);Assert.Contains("Descuentos: $0",summary);Assert.Contains("Total: $78.000",summary);Assert.EndsWith("¿Confirmas el pedido?",summary);
    }

    [Fact]
    public async Task Address_button_text_selects_only_active_same_branch_address()
    {
        await using var db=Db();var branch=new Branch{Id=1,Name="Centro"};var other=new Branch{Id=2,Name="Otra"};var validNeighborhood=new Neighborhood{Id=1,BranchId=1,Branch=branch,Name="Amazonia",Active=true};var otherNeighborhood=new Neighborhood{Id=2,BranchId=2,Branch=other,Name="Lejano",Active=true};var customer=new Customer{Id=7,BranchId=1,Name="Ana",Phone1="1"};
        db.AddRange(branch,other,validNeighborhood,otherNeighborhood,customer,new Address{Id=123,CustomerId=7,Neighborhood=validNeighborhood,AddressText="Cr 1"},new Address{Id=999,CustomerId=7,Neighborhood=otherNeighborhood,AddressText="Cr 9"},new WhatsAppConversation{Id=1,BranchId=1,CustomerId=7,PhoneNumber="1",AttentionMode=WhatsAppAttentionMode.Ai},new WhatsAppMessage{Id=1,ConversationId=1,Direction=WhatsAppMessageDirection.Inbound,Type=WhatsAppMessageType.Text,TextBody="Seleccionar dirección 123"});await db.SaveChangesAsync();
        var service=new WhatsAppDraftService(db,new WhatsAppOrderDraftCalculator(db));using var args=JsonDocument.Parse("{}");var tool=new SelectCustomerAddressAgentTool(service,db);
        Assert.True((await tool.ExecuteAsync(new(1,1,1,CustomerId:7),args.RootElement,default)).Success);Assert.Equal(123,(await db.WhatsAppOrderDrafts.SingleAsync()).AddressId);
        db.WhatsAppMessages.Find(1)!.TextBody="Seleccionar dirección 999";await db.SaveChangesAsync();var rejected=await tool.ExecuteAsync(new(1,1,1,CustomerId:7),args.RootElement,default);Assert.False(rejected.Success);Assert.Equal(123,(await db.WhatsAppOrderDrafts.SingleAsync()).AddressId);
    }

    private static ApplicationDbContext Db()=>new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
