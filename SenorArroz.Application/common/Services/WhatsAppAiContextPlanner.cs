using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Options;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Common.Services;

public class WhatsAppAiContextPlanner(IOptions<WhatsAppAiContextOptimizationOptions> options, ILogger<WhatsAppAiContextPlanner> logger) : IWhatsAppAiContextPlanner
{
    public const string RuntimeContextMarker = "ESTADO_OPERATIVO_ACTUAL";
    private static readonly string[] Always = ["request_human_assistance"];
    public Task<WhatsAppAiContextPlan> PlanAsync(WhatsAppAiContextPlannerInput i, CancellationToken ct=default)
    {
        try { return Task.FromResult(Build(i)); }
        catch(Exception ex) when(options.Value.FallbackToLegacyOnPlannerError)
        {
            logger.LogWarning(ex,"Context planner failed; using legacy tools ConversationId={ConversationId}",i.Conversation.Id);
            return Task.FromResult(Legacy(i,true,Sanitize(ex.Message)));
        }
    }
    private WhatsAppAiContextPlan Build(WhatsAppAiContextPlannerInput i)
    {
        if(!string.Equals(i.Strategy,"optimized_v1",StringComparison.OrdinalIgnoreCase)) return Legacy(i,false,null);
        var max=Math.Min(Math.Max(1,i.MaxContextMessages),Math.Max(1,options.Value.OptimizedMaxRecentMessages));
        var history=i.History.Where(x=>!string.IsNullOrWhiteSpace(x.Content)).TakeLast(max).ToList();
        if(!history.Any(x=>x.Role=="user"&&x.Content==i.IncomingMessage.TextBody)) history.Add(new("user",i.IncomingMessage.TextBody));
        history=history.TakeLast(max).ToList();
        var state=BuildState(i);
        var messages=new List<AiChatMessage>{new("system",i.SystemPrompt),new("system",RuntimeContextMarker+" (prevalece sobre el historial):\n"+state)};messages.AddRange(history);
        var names=SelectTools(i).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if(names.Count==0||!Always.All(x=>names.Contains(x,StringComparer.OrdinalIgnoreCase))) return Legacy(i,true,"tool_selection_incomplete");
        var selected=i.AllTools.Where(x=>names.Contains(x.Name,StringComparer.OrdinalIgnoreCase)).ToList();
        if(selected.Count!=names.Count) return Legacy(i,true,"unknown_tool_in_plan");
        return Metrics("optimized_v1",messages,state,names,history,i.SystemPrompt,selected,false,null);
    }
    private static WhatsAppAiContextPlan Legacy(WhatsAppAiContextPlannerInput i,bool fallback,string? reason){var messages=new List<AiChatMessage>{new("system",i.SystemPrompt)};messages.AddRange(i.History);return Metrics("legacy",messages,null,i.AllTools.Select(x=>x.Name).ToList(),i.History,i.SystemPrompt,i.AllTools,fallback,reason);}
    private static WhatsAppAiContextPlan Metrics(string strategy,IReadOnlyList<AiChatMessage> messages,string? state,IReadOnlyList<string> names,IReadOnlyList<AiChatMessage> history,string prompt,IReadOnlyList<AiToolDefinition> tools,bool fallback,string? reason)=>new(strategy,messages,state,names,history.Count,tools.Count,prompt.Length,state?.Length??0,history.Sum(x=>x.Content?.Length??0),tools.Sum(x=>x.Name.Length+x.Description.Length+x.ParametersSchema.GetRawText().Length),fallback,reason);
    private static IEnumerable<string> SelectTools(WhatsAppAiContextPlannerInput i)
    {
        var result=new HashSet<string>(Always,StringComparer.OrdinalIgnoreCase){"search_products","send_menu"};
        if(WhatsAppConversationPolicy.IsInformationRequest(i.IncomingMessage.TextBody)){result.Add("get_product_details");result.Add("send_product_details");}
        if(i.Customer is null){result.UnionWith(["find_customer_by_phone","create_customer","update_customer"]);return result;}
        result.UnionWith(["update_customer","list_customer_addresses"]);
        var d=i.ActiveDraft;if(d is null){result.Add("get_or_create_order_draft");return result;}
        result.UnionWith(["get_order_draft","add_draft_item","remove_draft_item","set_draft_notes","set_order_type","cancel_order_draft"]);
        if(d.Items.Count==0)return result;
        result.UnionWith(["list_customer_addresses","select_customer_address","create_customer_address","save_validated_customer_address","find_registered_neighborhood","resolve_address_with_maps","validate_delivery_address","calculate_delivery_fee"]);
        if(d.Status is WhatsAppOrderDraftStatus.AwaitingPayment or WhatsAppOrderDraftStatus.ReadyForConfirmation || string.IsNullOrWhiteSpace(d.PaymentMethod))result.UnionWith(["list_payment_methods","set_payment_method","calculate_cash_change"]);
        result.UnionWith(["get_order_confirmation_summary","mark_draft_ready_for_confirmation"]);
        return result;
    }
    private string BuildState(WhatsAppAiContextPlannerInput i)
    {
        var limit=Math.Max(1,options.Value.MaxRuntimeContextCharacters);
        var total=i.ActiveDraft?.Items.Count??0;
        for(var itemLimit=Math.Min(30,total);itemLimit>=0;itemLimit--)
        {
            var json=SerializeState(i,itemLimit,total);
            if(json.Length<=limit)return json;
        }
        throw new InvalidOperationException("runtime_context_limit_too_small");
    }
    private static string SerializeState(WhatsAppAiContextPlannerInput i,int itemLimit,int totalItemCount)
    {
        var d=i.ActiveDraft;var missing=d is null?[]:Missing(d);
        static string? Short(string? value,int max)=>string.IsNullOrWhiteSpace(value)?value:value[..Math.Min(max,value.Length)];
        var addresses=(i.Customer?.Addresses??[]).Where(x=>x.CustomerId==i.Customer!.Id).OrderByDescending(x=>x.IsPrimary).ThenBy(x=>x.Id).Take(3).Select(x=>new{addressId=x.Id,address=Short(x.AddressText,120),additionalInfo=Short(x.AdditionalInfo,120),neighborhood=Short(x.Neighborhood?.Name,80),isPrimary=x.IsPrimary,hasCoverage=x.Neighborhood?.Active==true&&x.Neighborhood.BranchId==i.Branch.Id}).ToList();var suggested=addresses.FirstOrDefault(x=>x.isPrimary&&x.hasCoverage)?.addressId??(addresses.Count(x=>x.hasCoverage)==1?addresses.First(x=>x.hasCoverage).addressId:(int?)null);
        var value=new{conversation=new{customerLinked=i.Customer is not null,attentionMode="ai"},savedAddresses=d?.OrderType==OrderType.Delivery?addresses:[],suggestedAddressId=suggested,draft=d is null?null:new{exists=true,status=d.Status.ToString(),orderType=d.OrderType?.ToString(),resumingExistingDraft=d.Items.Count>0,items=d.Items.Take(itemLimit).Select(x=>new{draftItemId=x.Id,productId=x.ProductId,name=Short(x.Product?.Name,80),quantity=x.Quantity,notes=Short(x.Notes,120)}),totalItemCount,itemsTruncated=itemLimit<totalItemCount,hasValidAddress=d.AddressId.HasValue&&d.Address?.Neighborhood?.Active==true&&d.Address.Neighborhood.BranchId==i.Branch.Id,neighborhood=Short(d.Address?.Neighborhood?.Name,80),deliveryFee=d.DeliveryFee,subtotal=d.Subtotal,total=d.Total,paymentMethod=d.PaymentMethod,missing,readyToConfirm=d.Status==WhatsAppOrderDraftStatus.ReadyForConfirmation}};
        return JsonSerializer.Serialize(value);
    }
    private static string[] Missing(SenorArroz.Domain.Entities.WhatsAppOrderDraft d)
    {
        var result=new List<string>();if(d.CustomerId is null)result.Add("customer");if(d.Items.Count==0)result.Add("items");if(d.OrderType is null)result.Add("orderType");if(d.OrderType==OrderType.Delivery&&d.AddressId is null)result.Add("address");if(string.IsNullOrWhiteSpace(d.PaymentMethod))result.Add("paymentMethod");return [..result];
    }
    private static string Sanitize(string value)=>value[..Math.Min(300,value.Length)];
}
