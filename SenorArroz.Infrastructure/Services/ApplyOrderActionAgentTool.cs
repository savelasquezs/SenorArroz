using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public sealed class ApplyOrderActionAgentTool(IWhatsAppSimpleOrderStateService states,ApplicationDbContext db):IAgentTool
{
    public string Name=>"apply_order_action";
    public string Description=>"Modifica el carrito simple con IDs reales del catálogo. El backend valida sucursal, disponibilidad, stock y cantidades; nunca acepta precios.";
    public string Category=>"order";public bool ModifiesData=>true;public string RiskLevel=>"medium";
    public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"action":{"type":"string","enum":["add_product","remove_product","set_quantity","clear_cart"]},"productId":{"type":"integer","minimum":1},"quantity":{"type":"integer","minimum":1,"maximum":50},"notes":{"type":"string","maxLength":500}},"required":["action"],"additionalProperties":false}""").RootElement.Clone();

    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement arguments,CancellationToken ct)
    {
        var action=arguments.GetProperty("action").GetString();
        if(action is not ("add_product" or "remove_product" or "set_quantity" or "clear_cart"))return Invalid("Acción no permitida.");
        int? productId=arguments.TryGetProperty("productId",out var productValue)&&productValue.TryGetInt32(out var parsedProduct)?parsedProduct:null;
        int? quantity=arguments.TryGetProperty("quantity",out var quantityValue)&&quantityValue.TryGetInt32(out var parsedQuantity)?parsedQuantity:null;
        if((action is "add_product" or "remove_product" or "set_quantity")&&!productId.HasValue)return Invalid("productId es requerido para esta acción.");
        if(action=="set_quantity"&&!quantity.HasValue)return Invalid("quantity es requerido para establecer la cantidad.");
        if(quantity is <1 or >50)return Invalid("La cantidad debe estar entre 1 y 50.");
        if(arguments.TryGetProperty("notes",out var notesValue)&&notesValue.GetString()?.Length>500)return Invalid("Las notas superan 500 caracteres.");

        var state=await states.LoadAsync(c.ConversationId,ct);var effectiveQuantity=quantity??1;
        var operationKey=$"{c.ExecutionId??$"msg-{c.IncomingMessageId??0}"}:{action}:{productId?.ToString()??"none"}:{effectiveQuantity}";
        if(state.AppliedOperationKeys.Contains(operationKey,StringComparer.Ordinal))
            return new(true,new{actionApplied=action,alreadyApplied=true,cart=await states.BuildSummaryAsync(c.BranchId,state,ct)},Code:"order_action_applied");

        Domain.Entities.Product? product=null;
        if(productId.HasValue)
        {
            product=await db.Products.AsNoTracking().Include(x=>x.Category).FirstOrDefaultAsync(x=>x.Id==productId&&x.Category.BranchId==c.BranchId,ct);
            if(product is null)return new(false,null,"El producto no pertenece a la sucursal.","product_not_found");
            if(!product.Active)return new(false,null,"El producto está inactivo.","product_unavailable");
        }

        var item=productId.HasValue?state.Items.FirstOrDefault(x=>x.ProductId==productId):null;
        if(action=="add_product")
        {
            var target=(item?.Quantity??0)+effectiveQuantity;if(target>50)return Invalid("La cantidad acumulada supera 50.");if(product!.Stock.HasValue&&product.Stock<target)return new(false,null,"Stock insuficiente.","insufficient_stock");
            if(item is null)state.Items.Add(new(){ProductId=product.Id,Quantity=effectiveQuantity,Notes=arguments.TryGetProperty("notes",out var n)?n.GetString()?.Trim():null});else{item.Quantity=target;if(arguments.TryGetProperty("notes",out var n))item.Notes=n.GetString()?.Trim();}
        }
        else if(action=="set_quantity")
        {
            if(product!.Stock.HasValue&&product.Stock<effectiveQuantity)return new(false,null,"Stock insuficiente.","insufficient_stock");if(item is null)state.Items.Add(new(){ProductId=product.Id,Quantity=effectiveQuantity});else item.Quantity=effectiveQuantity;
        }
        else if(action=="remove_product")state.Items.RemoveAll(x=>x.ProductId==productId);
        else state.Items.Clear();

        state.AppliedOperationKeys.Add(operationKey);await states.SaveAsync(c.ConversationId,state,ct);var summary=await states.BuildSummaryAsync(c.BranchId,state,ct);
        return new(true,new{actionApplied=action,alreadyApplied=false,product=product is null?null:new{productId=product.Id,product.Name,quantity=state.Items.FirstOrDefault(x=>x.ProductId==product.Id)?.Quantity??0},cart=summary},Code:"order_action_applied");
    }

    private static AgentToolExecutionResult Invalid(string error)=>new(false,null,error,"invalid_arguments");
}
