using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Common.Services;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

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

    private static async Task<WhatsAppAiContextPlan> Plan(string strategy="optimized_v1",WhatsAppOrderDraft? draft=null,Customer? customer=null,List<AiChatMessage>? history=null){using var schema=JsonDocument.Parse("""{"type":"object"}""");var tools=Names.Select(x=>new AiToolDefinition(x,x,schema.RootElement.Clone())).ToList();history??=[new("user","actual")];var c=new WhatsAppConversation{Id=1,BranchId=1,PhoneNumber="secret",AttentionMode=WhatsAppAttentionMode.Ai,CustomerId=customer?.Id};var m=new WhatsAppMessage{Id=2,ConversationId=1,TextBody="actual"};var planner=new WhatsAppAiContextPlanner(Options.Create(new WhatsAppAiContextOptimizationOptions()),NullLogger<WhatsAppAiContextPlanner>.Instance);return await planner.PlanAsync(new(c,m,new Branch{Id=1,Name="Branch"},customer,draft,strategy,20,history,tools,"stable prompt"));}
    private static WhatsAppOrderDraft Draft(WhatsAppOrderDraftStatus status,bool item){var d=new WhatsAppOrderDraft{Id=3,CustomerId=1,Status=status,OrderType=OrderType.Delivery};if(item)d.Items.Add(new WhatsAppOrderDraftItem{Id=4,ProductId=5,Quantity=1,Product=new Product{Name="Arroz",Active=true}});return d;}
}
