using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application;
using SenorArroz.Infrastructure;

namespace SenorArroz.Tests;
public class WhatsAppAiContextPlannerTests
{
    private static readonly string[] Names=["request_human_assistance","search_products","get_product_details","send_menu","send_product_details","find_customer_by_phone","create_customer","update_customer","list_customer_addresses","get_or_create_order_draft","get_order_draft","add_draft_item","remove_draft_item","set_draft_notes","set_order_type","cancel_order_draft","select_customer_address","create_customer_address","save_validated_customer_address","find_registered_neighborhood","resolve_address_with_maps","validate_delivery_address","calculate_delivery_fee","list_payment_methods","set_payment_method","calculate_cash_change","get_order_confirmation_summary","mark_draft_ready_for_confirmation"];
    [Fact] public async Task Legacy_keeps_all_tools(){var p=await Plan("legacy");Assert.Equal(Names.Length,p.ToolDefinitionCount);Assert.Equal("legacy",p.Strategy);}
    [Fact] public async Task Optimized_new_conversation_reduces_tools_but_keeps_products_and_human(){var p=await Plan();Assert.True(p.ToolDefinitionCount<Names.Length);Assert.Contains("search_products",p.AllowedToolNames);Assert.Contains("request_human_assistance",p.AllowedToolNames);}
    [Fact] public async Task Draft_without_items_keeps_product_mutation_tools(){var p=await Plan(draft:Draft(WhatsAppOrderDraftStatus.Building,false),customer:new Customer());Assert.Contains("add_draft_item",p.AllowedToolNames);Assert.DoesNotContain("set_payment_method",p.AllowedToolNames);}
    [Theory] [InlineData(WhatsAppOrderDraftStatus.AwaitingAddress)] [InlineData(WhatsAppOrderDraftStatus.AwaitingPayment)] [InlineData(WhatsAppOrderDraftStatus.ReadyForConfirmation)] public async Task Advanced_states_still_allow_previous_steps(WhatsAppOrderDraftStatus status){var p=await Plan(draft:Draft(status,true),customer:new Customer());Assert.Contains("remove_draft_item",p.AllowedToolNames);Assert.Contains("select_customer_address",p.AllowedToolNames);Assert.Contains("request_human_assistance",p.AllowedToolNames);}
    [Fact] public async Task Payment_state_exposes_payment_tools(){var p=await Plan(draft:Draft(WhatsAppOrderDraftStatus.AwaitingPayment,true),customer:new Customer());Assert.Contains("list_payment_methods",p.AllowedToolNames);Assert.Contains("calculate_cash_change",p.AllowedToolNames);}
    [Fact] public async Task History_is_chronological_limited_and_current_once(){var h=Enumerable.Range(1,12).Select(x=>new AiChatMessage(x%2==0?"assistant":"user",x==11?"actual":$"m{x}")).ToList();var p=await Plan(history:h);Assert.True(p.HistoryMessageCount<=8);Assert.Equal(1,p.Messages.Count(x=>x.Role=="user"&&x.Content=="actual"));}
    [Fact] public async Task Structured_state_is_compact_and_contains_no_phone(){var p=await Plan(draft:Draft(WhatsAppOrderDraftStatus.AwaitingPayment,true),customer:new Customer());Assert.Contains("ESTADO_OPERATIVO_ACTUAL",p.Messages[1].Content);Assert.DoesNotContain("phone",p.StructuredState!,StringComparison.OrdinalIgnoreCase);Assert.Contains("prevalece",p.Messages[1].Content!,StringComparison.OrdinalIgnoreCase);}
    [Fact] public async Task Legacy_is_immediate_fallback_switch(){var optimized=await Plan();var legacy=await Plan("legacy");Assert.True(legacy.ToolDefinitionCount>optimized.ToolDefinitionCount);}
    [Fact] public async Task Runtime_state_is_valid_json_and_respects_character_limit(){var draft=Draft(WhatsAppOrderDraftStatus.AwaitingPayment,false);for(var x=0;x<40;x++)draft.Items.Add(new WhatsAppOrderDraftItem{Id=x+1,ProductId=x+100,Quantity=2,Notes=new string('N',500),Product=new Product{Name=new string('P',300),Active=true}});var p=await Plan(draft:draft,customer:new Customer(),maxRuntimeCharacters:900);Assert.False(p.FallbackToLegacyTools);Assert.True(p.StructuredState!.Length<=900);using var json=JsonDocument.Parse(p.StructuredState);Assert.True(json.RootElement.GetProperty("draft").GetProperty("itemsTruncated").GetBoolean());Assert.Equal(40,json.RootElement.GetProperty("draft").GetProperty("totalItemCount").GetInt32());}
    [Fact] public async Task Runtime_state_falls_back_safely_when_minimum_json_cannot_fit(){var p=await Plan(draft:Draft(WhatsAppOrderDraftStatus.Building,true),customer:new Customer(),maxRuntimeCharacters:10);Assert.True(p.FallbackToLegacyTools);Assert.Equal("runtime_context_limit_too_small",p.FallbackReason);}

    [Fact]
    public async Task Real_DI_catalog_contains_every_tool_selected_for_operational_states()
    {
        var services=new ServiceCollection();services.AddLogging();services.AddApplication();
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection","Host=localhost;Database=test;Username=test;Password=test"}}).Build();
        services.AddInfrastructureServices(configuration);services.AddScoped(_=>Mock.Of<IWhatsAppAutomaticMessageSender>());services.AddScoped(_=>Mock.Of<IWhatsAppNotificationService>());
        await using var provider=services.BuildServiceProvider();await using var scope=provider.CreateAsyncScope();
        var catalog=scope.ServiceProvider.GetRequiredService<IAgentToolCatalog>();
        var planner=scope.ServiceProvider.GetRequiredService<IWhatsAppAiContextPlanner>();
        var registered=catalog.All.Select(x=>x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("list_payment_methods",registered);Assert.Contains("set_payment_method",registered);Assert.Contains("calculate_cash_change",registered);Assert.Contains("mark_draft_ready_for_confirmation",registered);
        var scenarios=new (Customer? customer,WhatsAppOrderDraft? draft)[]{(null,null),(new Customer{Id=1},null),(new Customer{Id=1},Draft(WhatsAppOrderDraftStatus.Building,false)),(new Customer{Id=1},Draft(WhatsAppOrderDraftStatus.Building,true)),(new Customer{Id=1},Draft(WhatsAppOrderDraftStatus.AwaitingAddress,true)),(new Customer{Id=1},Draft(WhatsAppOrderDraftStatus.AwaitingPayment,true)),(new Customer{Id=1},Draft(WhatsAppOrderDraftStatus.ReadyForConfirmation,true))};
        foreach(var scenario in scenarios){var plan=await PlanWith(planner,catalog.All,scenario.customer,scenario.draft);Assert.False(plan.FallbackToLegacyTools,plan.FallbackReason);Assert.All(plan.AllowedToolNames,name=>Assert.Contains(name,registered));Assert.Contains("request_human_assistance",plan.AllowedToolNames);}
    }

    private static async Task<WhatsAppAiContextPlan> Plan(string strategy="optimized_v1",WhatsAppOrderDraft? draft=null,Customer? customer=null,List<AiChatMessage>? history=null,int maxRuntimeCharacters=6000){using var schema=JsonDocument.Parse("""{"type":"object"}""");var tools=Names.Select(x=>new AiToolDefinition(x,x,schema.RootElement.Clone())).ToList();var planner=new WhatsAppAiContextPlanner(Options.Create(new WhatsAppAiContextOptimizationOptions{MaxRuntimeContextCharacters=maxRuntimeCharacters}),NullLogger<WhatsAppAiContextPlanner>.Instance);return await PlanWith(planner,tools,customer,draft,strategy,history);}
    private static Task<WhatsAppAiContextPlan> PlanWith(IWhatsAppAiContextPlanner planner,IReadOnlyList<AiToolDefinition> tools,Customer? customer,WhatsAppOrderDraft? draft,string strategy="optimized_v1",List<AiChatMessage>? history=null){history??=[new("user","actual")];var c=new WhatsAppConversation{Id=1,BranchId=1,PhoneNumber="secret",AttentionMode=WhatsAppAttentionMode.Ai,CustomerId=customer?.Id};var m=new WhatsAppMessage{Id=2,ConversationId=1,TextBody="actual"};return planner.PlanAsync(new(c,m,new Branch{Id=1,Name="Branch"},customer,draft,strategy,20,history,tools,"stable prompt"));}
    private static WhatsAppOrderDraft Draft(WhatsAppOrderDraftStatus status,bool item){var d=new WhatsAppOrderDraft{Id=3,CustomerId=1,Status=status,OrderType=OrderType.Delivery};if(item)d.Items.Add(new WhatsAppOrderDraftItem{Id=4,ProductId=5,Quantity=1,Product=new Product{Name="Arroz",Active=true}});return d;}
}
