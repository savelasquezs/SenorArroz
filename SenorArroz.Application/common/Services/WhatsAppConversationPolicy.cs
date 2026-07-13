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
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"(?i)\b(delicios[oa]s?|una combinaci[oó]n perfecta|te encantar[aá]|irresistible|exquisit[oa]s?)\b[!,. ]*")] private static partial Regex Commercial();
    [GeneratedRegex(@"[^.!?]+[.!?]?")] private static partial Regex Sentence();
}
