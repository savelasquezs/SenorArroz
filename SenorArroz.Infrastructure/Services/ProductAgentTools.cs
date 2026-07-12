using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Infrastructure.Data;
namespace SenorArroz.Infrastructure.Services;
public class SearchProductsAgentTool(ApplicationDbContext db) : IAgentTool
{
    public string Name=>"search_products"; public string Description=>"Busca productos reales de la sucursal por nombre, descripción, ingredientes o personas.";
    public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string"},"servesPeople":{"type":"integer","minimum":1}},"required":["query"]}""").RootElement.Clone();
    public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){var q=a.TryGetProperty("query",out var e)?e.GetString()??"":"";int? people=a.TryGetProperty("servesPeople",out var p)&&p.TryGetInt32(out var n)?n:null;var pattern=$"%{q}%";var query=db.Products.AsNoTracking().Include(x=>x.Category).Include(x=>x.CommercialProfile).Where(x=>x.Category.BranchId==c.BranchId&&(EF.Functions.ILike(x.Name,pattern)||(x.CommercialProfile!=null&&(EF.Functions.ILike(x.CommercialProfile.Name,pattern)||(x.CommercialProfile.Description!=null&&EF.Functions.ILike(x.CommercialProfile.Description,pattern))||(x.CommercialProfile.Ingredients!=null&&EF.Functions.ILike(x.CommercialProfile.Ingredients,pattern))))));if(people.HasValue)query=query.Where(x=>x.ServesPeopleMin<=people&&x.ServesPeopleMax>=people);var rows=await query.Take(10).Select(x=>new{x.Id,x.Name,x.Price,Available=x.Active&&(!x.Stock.HasValue||x.Stock>0),x.ServesPeopleMin,x.ServesPeopleMax,Description=x.CommercialProfile==null?null:x.CommercialProfile.Description,Ingredients=x.CommercialProfile==null?null:x.CommercialProfile.Ingredients}).ToListAsync(ct);return new(true,rows);}
}
public class ProductDetailsAgentTool(ApplicationDbContext db) : IAgentTool
{
 public string Name=>"get_product_details";public string Description=>"Consulta detalles, precio y disponibilidad actual de un producto por ID.";public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"productId":{"type":"integer"}},"required":["productId"]}""").RootElement.Clone();
 public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){if(!a.TryGetProperty("productId",out var p)||!p.TryGetInt32(out var id))return new(false,null,"productId requerido");var x=await db.Products.AsNoTracking().Include(x=>x.Category).Include(x=>x.CommercialProfile).Where(x=>x.Id==id&&x.Category.BranchId==c.BranchId).Select(x=>new{x.Id,x.Name,x.Price,Available=x.Active&&(!x.Stock.HasValue||x.Stock>0),x.ServesPeopleMin,x.ServesPeopleMax,Description=x.CommercialProfile==null?null:x.CommercialProfile.Description,Ingredients=x.CommercialProfile==null?null:x.CommercialProfile.Ingredients,PhotoUrl=x.CommercialProfile==null?null:x.CommercialProfile.PhotoUrl}).FirstOrDefaultAsync(ct);return x==null?new(false,null,"Producto no encontrado."):new(true,x);}
}
