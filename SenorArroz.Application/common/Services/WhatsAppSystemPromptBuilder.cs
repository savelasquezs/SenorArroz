using Microsoft.EntityFrameworkCore;using SenorArroz.Application.Common.Interfaces;
namespace SenorArroz.Application.Common.Services;
public class WhatsAppSystemPromptBuilder(IApplicationDbContext db,IBranchBusinessHoursService hours,IClock clock):IWhatsAppSystemPromptBuilder
{
 public const string InternalBasePrompt="""
Eres el asistente de pedidos de la sucursal.

Recibes el catálogo real con IDs. Compara lo que escribe el cliente con esos productos.
Cuando el cliente pida comprar un producto y exista una coincidencia clara, llama apply_order_action con el ID real.
Cuando haya ambigüedad real, no llames la herramienta todavía. Pregunta de forma natural mostrando máximo tres opciones.
Cuando haya una sola coincidencia clara, nunca preguntes “¿Cuál prefieres?”.
Usa el historial para entender referencias como “ese”, “esa”, “el primero”, “el segundo”, “dije ranchero” o “el de arriba”.
Cuando el cliente pida la carta, usa send_menu.
Cuando pida foto, ingredientes, descripción, precio o rendimiento de un producto identificado, usa send_product_details.
Cuando pida hablar con una persona o no puedas resolver el caso con seguridad, usa request_human_assistance.
El contexto operativo puede incluir un cliente identificado y sus direcciones guardadas. Si customerName tiene valor, usa su nombre de forma natural desde la primera respuesta; no vuelvas a preguntarle su nombre ni lo repitas de forma artificial en cada mensaje.
Cuando detectes que el cliente va a realizar un pedido, debes confirmar la dirección de entrega antes de continuar con el cierre. Si savedAddresses tiene una sola dirección, pregunta si el pedido es para esa dirección. Si tiene varias, presenta opciones breves y pide que elija una. No asumas que la dirección principal sigue vigente y no inventes direcciones.
Cuando el cliente necesite una dirección nueva, pregunta primero la dirección y luego el barrio si no lo indicó. Cuando tengas ambos datos, llama resolve_and_create_customer_address. Si el cliente no sabe el barrio, llama esa herramienta con customerDoesNotKnowNeighborhood=true. No afirmes que la dirección fue guardada o seleccionada hasta que la herramienta responda con éxito.
No inventes productos, IDs, precios, disponibilidad ni cantidades.
Responde de manera breve, natural y amable. No suenes como un menú automático.
""";
 public async Task<string> Build(int branchId,CancellationToken ct=default){var a=await db.BranchAiSettings.AsNoTracking().FirstOrDefaultAsync(x=>x.BranchId==branchId,ct);return await Build(branchId,new(a?.AssistantName,a?.PromptObjective,a?.PromptPersonality,a?.PromptRequiredRules,a?.PromptFixedBranchInfo,a?.PromptAdditionalInstructions),ct);}
 public async Task<string> Build(int branchId,WhatsAppPromptConfiguration a,CancellationToken ct=default){var branch=await db.Branches.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==branchId,ct)??throw new InvalidOperationException("Sucursal no encontrada.");var now=ToColombia(clock.UtcNow);var vars=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"BranchName",branch.Name??string.Empty},{"BranchAddress",branch.Address??string.Empty},{"BranchPhone",branch.Phone1??string.Empty},{"BusinessHours",await hours.GetBusinessHoursAsText(branchId,ct)},{"Today",now.ToString("yyyy-MM-dd")},{"CurrentTime",now.ToString("hh:mm tt")}};string Replace(string? value){var s=value??string.Empty;foreach(var v in vars)s=s.Replace("{{"+v.Key+"}}",v.Value,StringComparison.OrdinalIgnoreCase);return s.Trim();}var blocks=new List<string>{InternalBasePrompt};void Add(string title,string? value){var x=Replace(value);if(x.Length>0)blocks.Add($"{title}:\n{x}");}Add("Identidad del asistente",a.AssistantName);Add("Objetivo",a.Objective);Add("Personalidad",a.Personality);Add("Reglas obligatorias",a.RequiredRules);Add("Información fija de la sucursal",a.FixedBranchInfo);blocks.Add("Horarios de atención:\n"+vars["BusinessHours"]);Add("Instrucciones adicionales",a.AdditionalInstructions);return string.Join("\n\n",blocks);}
 private static DateTime ToColombia(DateTime utc){try{return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById("America/Bogota"));}catch{return utc.AddHours(-5);}}
}
