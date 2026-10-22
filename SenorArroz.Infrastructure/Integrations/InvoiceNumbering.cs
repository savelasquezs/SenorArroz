using System.Globalization;
using System.Text.Json;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Infrastructure.Integrations;

public sealed record InvoiceNumbering(string Resolution, string Prefix, long From, long To, DateTime? ValidUntil, long? NextNumber)
{
    private static string? Text(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
            foreach (var property in value.EnumerateObject())
                if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    return property.Value.ToString();
        return null;
    }
    private static long? Number(JsonElement value, params string[] keys) => long.TryParse(Text(value, keys), out var number) ? number : null;
    public static IReadOnlyList<InvoiceNumbering> Read(JsonElement value)
    {
        var result = new List<InvoiceNumbering>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array) { foreach (var child in node.EnumerateArray()) Visit(child); return; }
            if (node.ValueKind != JsonValueKind.Object) return;
            var resolution = Text(node, "resolution", "resolution_number");
            var prefix = Text(node, "prefix");
            var from = Number(node, "from", "from_number", "range_from");
            var to = Number(node, "to", "to_number", "range_to");
            if (resolution is not null && prefix is not null && from.HasValue && to.HasValue)
            {
                var date = Text(node, "date_to", "to_date", "valid_until");
                DateTime? expiry = DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
                var next = Number(node, "next_number", "nextNumber");
                var current = Number(node, "current", "current_number", "lastNumberUsed");
                if (!next.HasValue && current.HasValue && current.Value < long.MaxValue) next = Math.Max(from.Value, current.Value + 1);
                result.Add(new(resolution, prefix, from.Value, to.Value, expiry, next));
                return;
            }
            foreach (var property in node.EnumerateObject()) Visit(property.Value);
        }
        Visit(value);
        return result;
    }
    public static long? ReadNext(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (ReadNext(item) is long number) return number;
            return null;
        }
        if (value.ValueKind != JsonValueKind.Object) return null;
        var next = Number(value, "next_number", "nextNumber");
        if (next.HasValue) return next;
        var current = Number(value, "current", "current_number", "lastNumberUsed");
        if (!current.HasValue && value.TryGetProperty("invoice", out var invoice)) current = Number(invoice, "number");
        if (current.HasValue && current.Value < long.MaxValue) return current.Value + 1;
        foreach (var property in value.EnumerateObject())
        {
            next = ReadNext(property.Value);
            if (next.HasValue) return next;
        }
        return null;
    }
    public static void Validate(ElectronicInvoiceIntegration integration, IReadOnlyList<InvoiceNumbering> rows, JsonElement? remaining, DateTime now, bool numberingConfirmed = false)
    {
        var matches = rows.Where(r => r.Resolution == integration.Resolution && r.Prefix == integration.Prefix).ToList();
        if (matches.Count != 1) throw new BusinessException("La resolución y el prefijo no coinciden con una numeración de Plemsi. Consulta y selecciona la numeración correcta.");
        var row = matches[0];
        var next = remaining.HasValue ? ReadNext(remaining.Value) ?? row.NextNumber : row.NextNumber;
        if (row.ValidUntil is null || row.ValidUntil.Value.Date < now.AddHours(-5).Date
            || integration.ValidUntil is null || integration.ValidUntil.Value.Date > row.ValidUntil.Value.Date
            || row.From < 1 || row.To < row.From || integration.LastNumber > row.To
            || integration.NextNumber < row.From || integration.NextNumber > integration.LastNumber)
            throw new BusinessException("El rango o la vigencia configurados no coinciden con la numeración autorizada por Plemsi.");
        if (!next.HasValue && !numberingConfirmed) throw new BusinessException("Plemsi no confirmó el próximo consecutivo. Verifica el último utilizado en DIAN y en tu proveedor anterior, configura uno disponible y confirma la numeración dedicada antes de activar.");
        if (next.HasValue && integration.NextNumber < Math.Max(row.From, next.Value))
            throw new BusinessException("El consecutivo configurado ya pudo ser utilizado. Consulta los folios y configura un consecutivo disponible.");
    }
}
