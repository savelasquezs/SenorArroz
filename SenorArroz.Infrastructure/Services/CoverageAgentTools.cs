using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Models;
using SenorArroz.Application.Options;
using SenorArroz.Infrastructure.Data;

namespace SenorArroz.Infrastructure.Services;

public record NeighborhoodMatch(int Id,string Name,string BranchName,int DeliveryFee,bool RequiresBranchReassignment);
public record NeighborhoodResolution(bool Matched,bool RequiresConfirmation,NeighborhoodMatch? Match,IReadOnlyList<NeighborhoodMatch> Options,string? SuggestedQuestion);

public class RegisteredNeighborhoodResolver(ApplicationDbContext db)
{
 public async Task<NeighborhoodResolution> Resolve(string query,int conversationBranchId,CancellationToken ct)
 {
  var sought=Normalize(query);if(sought.Length<2)return new(false,false,null,[],null);
  var rows=await db.Neighborhoods.AsNoTracking().Where(x=>x.Active).Include(x=>x.Branch).Select(x=>new{x.Id,x.Name,x.BranchId,BranchName=x.Branch.Name,x.DeliveryFee}).ToListAsync(ct);
  var ranked=rows.Select(x=>new{Row=x,Score=Score(sought,Normalize(x.Name))}).Where(x=>x.Score>=0.68).OrderByDescending(x=>x.Score).ThenBy(x=>x.Row.Name).Take(5).ToList();
  if(ranked.Count==0)return new(false,false,null,[],null);
  var options=ranked.Select(x=>new NeighborhoodMatch(x.Row.Id,x.Row.Name,x.Row.BranchName,x.Row.DeliveryFee,x.Row.BranchId!=conversationBranchId)).ToList();
  var safe=ranked[0].Score>=0.82&&(ranked.Count==1||ranked[0].Score-ranked[1].Score>=0.12);if(safe)return new(true,false,options[0],[],null);
  var names=string.Join(" o ",options.Take(3).Select(x=>x.Name));return new(false,true,null,options.Take(3).ToList(),$"¿Te encuentras en {names}?");
 }
 private static string Normalize(string value){var s=value.ToLowerInvariant().Normalize(NormalizationForm.FormD);var b=new StringBuilder();foreach(var c in s)if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)b.Append(char.IsLetterOrDigit(c)?c:' ');var words=b.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries).Where(x=>x is not("barrio" or "sector" or "por" or "en" or "vivo" or "estoy" or "para" or "es"));return string.Join(' ',words);}
 private static double Score(string a,string b){if(a==b)return 1;if(b.Contains(a)||a.Contains(b))return .91;var distance=Levenshtein(a,b);return 1d-(double)distance/Math.Max(a.Length,b.Length);}
 private static int Levenshtein(string a,string b){var row=Enumerable.Range(0,b.Length+1).ToArray();for(var i=1;i<=a.Length;i++){var previous=row[0];row[0]=i;for(var j=1;j<=b.Length;j++){var old=row[j];row[j]=Math.Min(Math.Min(row[j]+1,row[j-1]+1),previous+(a[i-1]==b[j-1]?0:1));previous=old;}}return row[b.Length];}
}

public record GeocodedAddress(string FormattedAddress,decimal Latitude,decimal Longitude,string? Neighborhood,string Quality,bool RequiresConfirmation);
public class GoogleAddressGeocoder(HttpClient http,IOptions<GoogleMapsRouteOptions> options)
{
 public async Task<(GeocodedAddress? Result,string? Error)> Resolve(string? address,decimal? latitude,decimal? longitude,CancellationToken ct)
 {
  var key=options.Value.GeocodingApiKey;if(string.IsNullOrWhiteSpace(key))return(null,"Google Maps Geocoding no está configurado.");var target=latitude.HasValue&&longitude.HasValue?$"latlng={latitude.Value.ToString(CultureInfo.InvariantCulture)},{longitude.Value.ToString(CultureInfo.InvariantCulture)}":$"address={Uri.EscapeDataString(address??string.Empty)}";var url=$"https://maps.googleapis.com/maps/api/geocode/json?{target}&key={Uri.EscapeDataString(key)}&language=es&region=co";using var response=await http.GetAsync(url,ct);if(!response.IsSuccessStatusCode)return(null,"No fue posible consultar Google Maps.");using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var results=doc.RootElement.GetProperty("results");if(results.GetArrayLength()==0)return(null,"Google Maps no encontró la dirección.");var first=results[0];var location=first.GetProperty("geometry").GetProperty("location");string? neighborhood=null;foreach(var component in first.GetProperty("address_components").EnumerateArray()){var types=component.GetProperty("types").EnumerateArray().Select(x=>x.GetString()).ToHashSet();if(types.Contains("neighborhood")||types.Contains("sublocality")||types.Contains("sublocality_level_1")){neighborhood=component.GetProperty("long_name").GetString();break;}}var quality=first.TryGetProperty("partial_match",out var partial)&&partial.GetBoolean()?"partial":"exact";return(new(first.GetProperty("formatted_address").GetString()??string.Empty,location.GetProperty("lat").GetDecimal(),location.GetProperty("lng").GetDecimal(),neighborhood,quality,quality!="exact"),null);
 }
}

public class FindRegisteredNeighborhoodAgentTool(RegisteredNeighborhoodResolver resolver):IAgentTool
{
 public string Name=>"find_registered_neighborhood";public string Description=>"Busca un barrio registrado para confirmar cobertura, sucursal y valor real del domicilio. No permite elegir sucursal.";public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","minLength":2,"maxLength":150}},"required":["query"],"additionalProperties":false}""").RootElement.Clone();
 public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){if(!a.TryGetProperty("query",out var q)||string.IsNullOrWhiteSpace(q.GetString()))return new(false,null,"query es requerido.");var r=await resolver.Resolve(q.GetString()!,c.BranchId,ct);return new(true,new{r.Matched,r.RequiresConfirmation,neighborhoodId=r.Match?.Id,name=r.Match?.Name,branchIdResolvedInternally=r.Match!=null,branchName=r.Match?.BranchName,deliveryFee=r.Match?.DeliveryFee,requiresBranchReassignment=r.Match?.RequiresBranchReassignment,options=r.Options.Select(x=>new{x.Id,x.Name}),r.SuggestedQuestion});}
}

public class ResolveAddressWithMapsAgentTool(GoogleAddressGeocoder geocoder,RegisteredNeighborhoodResolver resolver):IAgentTool
{
 public string Name=>"resolve_address_with_maps";public string Description=>"Normaliza una dirección o ubicación con Google Maps y confirma cobertura únicamente contra barrios registrados.";public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"address":{"type":"string","maxLength":500},"latitude":{"type":"number"},"longitude":{"type":"number"}},"additionalProperties":false}""").RootElement.Clone();
 public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){var address=a.TryGetProperty("address",out var ad)?ad.GetString():null;decimal? lat=a.TryGetProperty("latitude",out var la)&&la.TryGetDecimal(out var lav)?lav:null;decimal? lng=a.TryGetProperty("longitude",out var lo)&&lo.TryGetDecimal(out var lov)?lov:null;if(string.IsNullOrWhiteSpace(address)&&(!lat.HasValue||!lng.HasValue))return new(false,null,"Se requiere address o latitude y longitude.");var(g,error)=await geocoder.Resolve(address,lat,lng,ct);if(g==null)return new(false,null,error);var coverage=string.IsNullOrWhiteSpace(g.Neighborhood)?new NeighborhoodResolution(false,false,null,[],null):await resolver.Resolve(g.Neighborhood,c.BranchId,ct);return new(true,new{g.FormattedAddress,g.Latitude,g.Longitude,detectedNeighborhood=g.Neighborhood,g.Quality,requiresAddressConfirmation=g.RequiresConfirmation,hasCoverage=coverage.Matched,coverage.RequiresConfirmation,neighborhoodId=coverage.Match?.Id,registeredNeighborhood=coverage.Match?.Name,branchName=coverage.Match?.BranchName,deliveryFee=coverage.Match?.DeliveryFee,requiresBranchReassignment=coverage.Match?.RequiresBranchReassignment,options=coverage.Options.Select(x=>new{x.Id,x.Name}),coverage.SuggestedQuestion});}
}

public class ValidateDeliveryAddressAgentTool(ApplicationDbContext db,RegisteredNeighborhoodResolver resolver,GoogleAddressGeocoder geocoder):IAgentTool
{
 public string Name=>"validate_delivery_address";public string Description=>"Valida una dirección guardada, barrio mencionado, dirección escrita o coordenadas. El precio y la sucursal siempre provienen del barrio registrado.";public JsonElement ParametersSchema=>JsonDocument.Parse("""{"type":"object","properties":{"addressId":{"type":"integer"},"neighborhood":{"type":"string"},"address":{"type":"string"},"latitude":{"type":"number"},"longitude":{"type":"number"}},"additionalProperties":false}""").RootElement.Clone();
 public async Task<AgentToolExecutionResult> ExecuteAsync(AgentToolExecutionContext c,JsonElement a,CancellationToken ct){var conversation=await db.WhatsAppConversations.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==c.ConversationId,ct);if(conversation==null)return new(false,null,"Conversación no encontrada.");if(a.TryGetProperty("addressId",out var aid)&&aid.TryGetInt32(out var id)){var saved=await db.Addresses.AsNoTracking().Include(x=>x.Neighborhood).ThenInclude(x=>x.Branch).FirstOrDefaultAsync(x=>x.Id==id&&x.CustomerId==conversation.CustomerId,ct);if(saved==null)return new(false,null,"Dirección no encontrada para el cliente de la conversación.");return new(true,new{hasCoverage=true,address=saved.AddressText,saved.Latitude,saved.Longitude,neighborhoodId=saved.NeighborhoodId,registeredNeighborhood=saved.Neighborhood.Name,branchName=saved.Neighborhood.Branch.Name,deliveryFee=saved.Neighborhood.DeliveryFee,requiresConfirmation=false,requiresBranchReassignment=saved.Neighborhood.BranchId!=c.BranchId,source="saved_address"});}if(a.TryGetProperty("neighborhood",out var n)&&!string.IsNullOrWhiteSpace(n.GetString())){var match=await resolver.Resolve(n.GetString()!,c.BranchId,ct);if(match.Matched)return new(true,new{hasCoverage=true,neighborhoodId=match.Match!.Id,registeredNeighborhood=match.Match.Name,branchName=match.Match.BranchName,deliveryFee=match.Match.DeliveryFee,requiresConfirmation=false,requiresBranchReassignment=match.Match.RequiresBranchReassignment,source="registered_neighborhood"});if(match.RequiresConfirmation)return new(true,new{hasCoverage=false,requiresConfirmation=true,options=match.Options.Select(x=>new{x.Id,x.Name}),match.SuggestedQuestion});}var address=a.TryGetProperty("address",out var ad)?ad.GetString():null;decimal? lat=a.TryGetProperty("latitude",out var la)&&la.TryGetDecimal(out var lav)?lav:null;decimal? lng=a.TryGetProperty("longitude",out var lo)&&lo.TryGetDecimal(out var lov)?lov:null;var(g,error)=await geocoder.Resolve(address,lat,lng,ct);if(g==null)return new(false,new{hasCoverage=false,rejectionReason=error},error);if(string.IsNullOrWhiteSpace(g.Neighborhood))return new(true,new{hasCoverage=false,g.FormattedAddress,g.Latitude,g.Longitude,requiresConfirmation=true,rejectionReason="Google Maps no identificó un barrio o sector."});var resolved=await resolver.Resolve(g.Neighborhood,c.BranchId,ct);return new(true,new{hasCoverage=resolved.Matched,g.FormattedAddress,g.Latitude,g.Longitude,detectedNeighborhood=g.Neighborhood,neighborhoodId=resolved.Match?.Id,registeredNeighborhood=resolved.Match?.Name,branchName=resolved.Match?.BranchName,deliveryFee=resolved.Match?.DeliveryFee,requiresConfirmation=resolved.RequiresConfirmation||g.RequiresConfirmation,requiresBranchReassignment=resolved.Match?.RequiresBranchReassignment,options=resolved.Options.Select(x=>new{x.Id,x.Name}),rejectionReason=resolved.Matched?null:"El barrio detectado no está registrado; no hay cobertura automática.",source="google_maps"});}
}
