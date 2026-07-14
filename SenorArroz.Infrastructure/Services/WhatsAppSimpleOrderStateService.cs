using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class WhatsAppSimpleOrderStateService(ApplicationDbContext db,IClock clock):IWhatsAppSimpleOrderStateService
{
    private static readonly JsonSerializerOptions JsonOptions=CreateJsonOptions();

    public async Task<WhatsAppSimpleOrderState> LoadAsync(int conversationId,CancellationToken ct=default)
    {
        var conversation=await db.WhatsAppConversations.FirstAsync(x=>x.Id==conversationId,ct);
        if(conversation.AiOrderStateUpdatedAt.HasValue&&conversation.AiOrderStateUpdatedAt.Value<clock.UtcNow.AddMinutes(-60))
        {
            conversation.AiOrderState=null;conversation.AiOrderStateUpdatedAt=null;await db.SaveChangesAsync(ct);return Empty();
        }
        if(string.IsNullOrWhiteSpace(conversation.AiOrderState))return Empty();
        try
        {
            var state=JsonSerializer.Deserialize<WhatsAppSimpleOrderState>(conversation.AiOrderState,JsonOptions);
            if(state is null||state.Version!=1||state.Items.Any(x=>x.ProductId<=0||x.Quantity is<1 or>50))throw new JsonException("Estado de carrito inválido.");
            state.Items=state.Items.GroupBy(x=>x.ProductId).Select(g=>new WhatsAppSimpleOrderItem{ProductId=g.Key,Quantity=Math.Min(50,g.Sum(x=>x.Quantity)),Notes=g.Last().Notes}).ToList();
            state.AppliedOperationKeys=state.AppliedOperationKeys.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).TakeLast(20).ToList();
            if(state.SelectedAddressId<=0)state.SelectedAddressId=null;
            if(state.OrderType.HasValue&&!Enum.IsDefined(state.OrderType.Value))throw new JsonException("Tipo de pedido inválido.");
            return state;
        }
        catch(JsonException)
        {
            conversation.AiOrderState=null;conversation.AiOrderStateUpdatedAt=null;await db.SaveChangesAsync(ct);return Empty();
        }
    }

    public async Task SaveAsync(int conversationId,WhatsAppSimpleOrderState state,CancellationToken ct=default)
    {
        state.Version=1;state.UpdatedAt=clock.UtcNow;state.AppliedOperationKeys=state.AppliedOperationKeys.Distinct(StringComparer.Ordinal).TakeLast(20).ToList();
        var conversation=await db.WhatsAppConversations.FirstAsync(x=>x.Id==conversationId,ct);
        conversation.AiOrderState=JsonSerializer.Serialize(state,JsonOptions);conversation.AiOrderStateUpdatedAt=clock.UtcNow;await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(int conversationId,CancellationToken ct=default)
    {
        var conversation=await db.WhatsAppConversations.FirstAsync(x=>x.Id==conversationId,ct);conversation.AiOrderState=null;conversation.AiOrderStateUpdatedAt=null;await db.SaveChangesAsync(ct);
    }

    public async Task<WhatsAppSimpleOrderSummary> BuildSummaryAsync(int branchId,WhatsAppSimpleOrderState state,CancellationToken ct=default)
    {
        var ids=state.Items.Select(x=>x.ProductId).ToList();
        var products=await db.Products.AsNoTracking().Include(x=>x.Category).Where(x=>ids.Contains(x.Id)&&x.Category.BranchId==branchId).ToDictionaryAsync(x=>x.Id,ct);
        var items=state.Items.Where(x=>products.ContainsKey(x.ProductId)).Select(x=>{var p=products[x.ProductId];return new WhatsAppSimpleOrderSummaryItem(p.Id,p.Name,x.Quantity,p.Price,p.Price*x.Quantity,p.Active&&(!p.Stock.HasValue||p.Stock>=x.Quantity));}).ToList();
        return new(items,items.Sum(x=>x.Subtotal),items.Sum(x=>x.Quantity));
    }

    private WhatsAppSimpleOrderState Empty()=>new(){UpdatedAt=clock.UtcNow};

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
