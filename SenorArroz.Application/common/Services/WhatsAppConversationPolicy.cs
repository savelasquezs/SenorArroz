using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SenorArroz.Application.Common.Services;
public static partial class WhatsAppConversationPolicy
{
    private static readonly string[] Purchase=["dame","quiero","agrega","agregame","voy a pedir","ponme","mandame"];
    private static readonly string[] Information=["foto","ingrediente","descripcion","precio","que contiene","que trae","cuanto rinde","informacion","detalle"];
    private static readonly string[] Pickup=["voy a recoger","recojo en el local","paso por el pedido","para recoger","no necesito domicilio"];
    public static string Normalize(string? value)
    {
        var decomposed=(value??string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder=new StringBuilder();foreach(var c in decomposed)if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)builder.Append(char.IsLetterOrDigit(c)?c:' ');
        return Spaces().Replace(builder.ToString().Normalize(NormalizationForm.FormC)," ").Trim();
    }
    public static bool IsPurchaseIntent(string? text){var n=Normalize(text);return Purchase.Any(x=>n.Contains(x,StringComparison.Ordinal))&&!IsInformationRequest(text);}
    public static bool IsInformationRequest(string? text){var n=Normalize(text);return Information.Any(x=>n.Contains(x,StringComparison.Ordinal));}
    public static bool IsExplicitPickupRequest(string? text){var n=Normalize(text);return Pickup.Any(x=>n.Contains(x,StringComparison.Ordinal));}
    public static string EnforceShortOrdinaryResponse(string text)
    {
        var clean=Commercial().Replace(text.Trim(),string.Empty);clean=Spaces().Replace(clean," ").Trim(' ',',','.');
        var questionSeen=false;var chars=clean.ToCharArray();for(var i=0;i<chars.Length;i++)if(chars[i]=='?'){if(questionSeen)chars[i]='.';else questionSeen=true;}
        clean=new string(chars);var sentences=Sentence().Matches(clean).Select(x=>x.Value.Trim()).Where(x=>x.Length>0).Take(2).ToList();clean=string.Join(" ",sentences);
        if(clean.Length>250)clean=clean[..247].TrimEnd()+"...";return clean;
    }
    public static string EnforceToolAwareResponse(string text,string? lastToolResult)
    {
        if(string.IsNullOrWhiteSpace(lastToolResult))return EnforceShortOrdinaryResponse(text);
        try
        {
            using var document=System.Text.Json.JsonDocument.Parse(lastToolResult);
            var root=document.RootElement;
            var code=Property(root,"Code").GetString();
            if(code=="menu_sent")return "¿Cuál deseas pedir?";
            if(code=="product_details_sent")return "¿Lo agrego?";
            if(code=="products_found")return FormatProducts(Property(root,"Data"));
            if(code=="confirmation_required")return FormatConfirmation(Property(root,"Data"));
        }
        catch(System.Text.Json.JsonException){}
        return EnforceShortOrdinaryResponse(text);
    }
    private static string FormatProducts(System.Text.Json.JsonElement data)
    {
        var products=Property(data,"Products");
        if(products.ValueKind!=System.Text.Json.JsonValueKind.Array)return "¿Cuál producto prefieres?";
        var names=products.EnumerateArray().Take(3).Select(x=>Property(x,"Name").GetString()).Where(x=>!string.IsNullOrWhiteSpace(x)).ToList();
        if(names.Count==0)return "No encontré opciones para esa cantidad. ¿Deseas buscar por nombre?";
        var result=string.Join("\n",names.Select(x=>$"- {x}"))+"\n¿Cuál prefieres?";
        return result.Length<=300?result:result[..297]+"...";
    }
    private static string FormatConfirmation(System.Text.Json.JsonElement data)
    {
        var summary=Property(data,"summary");if(summary.ValueKind!=System.Text.Json.JsonValueKind.Object)summary=data;
        var items=Property(summary,"items");var lines=new List<string>{"Resumen del pedido:"};
        if(items.ValueKind==System.Text.Json.JsonValueKind.Array)foreach(var item in items.EnumerateArray())
        {
            var name=Property(item,"name").GetString()??"Producto";if(name.Length>32)name=name[..32];
            lines.Add($"{Number(item,"Quantity")} x {name}: ${Money(Number(item,"Subtotal"))}");
        }
        lines.Add($"Domicilio: ${Money(Number(summary,"DeliveryFee"))}");
        lines.Add($"Descuentos: ${Money(Number(summary,"DiscountTotal"))}");
        lines.Add($"Total: ${Money(Number(summary,"Total"))}");
        lines.Add("¿Confirmas el pedido?");
        var result=string.Join("\n",lines);return result.Length<=4000?result:result[..3997]+"...";
    }
    private static int Number(System.Text.Json.JsonElement element,string name){var value=Property(element,name);return value.TryGetInt32(out var number)?number:0;}
    private static string Money(int value)=>value.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("es-CO"));
    private static System.Text.Json.JsonElement Property(System.Text.Json.JsonElement element,string name)
    {
        if(element.ValueKind==System.Text.Json.JsonValueKind.Object)foreach(var property in element.EnumerateObject())if(property.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return property.Value;
        return default;
    }
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"(?i)\b(delicios[oa]s?|una combinaci[oó]n perfecta|te encantar[aá]|irresistible|exquisit[oa]s?)\b[!,. ]*")] private static partial Regex Commercial();
    [GeneratedRegex(@"[^.!?]+[.!?]?")] private static partial Regex Sentence();
}
