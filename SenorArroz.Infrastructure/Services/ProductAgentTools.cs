using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.Infrastructure.Services;
public class SearchProductsAgentTool(IWhatsAppProductMatcher matcher):IAgentTool
{
 public string Name=>"search_products";public string Description=>"Resuelve productos reales de la sucursal por nombre aproximado o cantidad de personas. Usa la frase del cliente; query es opcional si servesPeople está presente.";
 public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","maxLength":150},"servesPeople":{"type":"integer","minimum":1},"limit":{"type":"integer","minimum":1,"maximum":10}},"anyOf":[{"required":["query"]},{"required":["servesPeople"]}],"additionalProperties":false}""").RootElement.Clone();
 public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){var q=a.TryGetProperty("query",out var e)?e.GetString():null;int? people=a.TryGetProperty("servesPeople",out var p)&&p.TryGetInt32(out var n)?n:null;if(string.IsNullOrWhiteSpace(q)&&!people.HasValue)return new(false,null,"query o servesPeople requerido.","invalid_arguments");var limit=a.TryGetProperty("limit",out var l)&&l.TryGetInt32(out var take)?Math.Clamp(take,1,10):10;var result=await matcher.MatchAsync(c.BranchId,q,people,limit,ct);return new(true,result,null,result.Products.Count==0?"product_not_found":"products_found");}
}
public class ProductDetailsAgentTool(ApplicationDbContext db):IAgentTool
{
 public string Name=>"get_product_details";public string Description=>"Consulta detalles, precio y disponibilidad actual de un producto por ID solo cuando el cliente pide información.";public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"productId":{"type":"integer"}},"required":["productId"],"additionalProperties":false}""").RootElement.Clone();
 public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){if(!a.TryGetProperty("productId",out var p)||!p.TryGetInt32(out var id))return new(false,null,"productId requerido");var x=await db.Products.AsNoTracking().Include(x=>x.Category).Include(x=>x.CommercialProfile).Where(x=>x.Id==id&&x.Category.BranchId==c.BranchId).Select(x=>new{x.Id,x.Name,x.Price,Available=x.Active&&(!x.Stock.HasValue||x.Stock>0),x.ServesPeopleMin,x.ServesPeopleMax,Description=x.CommercialProfile==null?null:x.CommercialProfile.Description,Ingredients=x.CommercialProfile==null?null:x.CommercialProfile.Ingredients,PhotoUrl=x.CommercialProfile==null?null:x.CommercialProfile.PhotoUrl}).FirstOrDefaultAsync(ct);return x==null?new(false,null,"Producto no encontrado."):new(true,x);}
}
