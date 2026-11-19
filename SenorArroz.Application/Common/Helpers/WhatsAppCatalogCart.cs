using System.Globalization;
using System.Text;
using System.Text.Json;
using SenorArroz.Application.Options;

namespace SenorArroz.Application.Common.Helpers;

public sealed record WhatsAppCatalogCart(string CatalogId, string? Note, IReadOnlyList<WhatsAppCatalogCart.Item> Items)
{
    public const string ManualReview = "catalog_cart_received";
    private static readonly CultureInfo Colombian = CultureInfo.GetCultureInfo("es-CO");

    public sealed record Item(string RetailerId, int Quantity, decimal? UnitPrice, string? Currency);

    public static WhatsAppCatalogCart? Read(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            return Read(document.RootElement);
        }
        catch (JsonException) { return null; }
    }

    public static WhatsAppCatalogCart? Read(JsonElement message)
    {
        if (!string.Equals(Field(message, "type"), "order", StringComparison.OrdinalIgnoreCase)
            || !message.TryGetProperty("order", out var order) || order.ValueKind != JsonValueKind.Object)
            return null;
        var catalogId = Field(order, "catalog_id");
        if (string.IsNullOrWhiteSpace(catalogId) || !order.TryGetProperty("product_items", out var lines)
            || lines.ValueKind != JsonValueKind.Array)
            return null;

        var items = new List<Item>();
        foreach (var line in lines.EnumerateArray())
        {
            var retailerId = Field(line, "product_retailer_id");
            if (string.IsNullOrWhiteSpace(retailerId) || !int.TryParse(Field(line, "quantity"),
                NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity is < 1 or > 999)
                continue;

            var price = decimal.TryParse(Field(line, "item_price"), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount) && amount is >= 0 and <= int.MaxValue ? amount : (decimal?)null;
            var currency = Field(line, "currency")?.ToUpperInvariant();
            items.Add(new(retailerId, quantity, price, currency is { Length: 3 } && currency.All(char.IsAsciiLetterUpper)
                ? currency : null));
        }
        return items.Count == 0 ? null : new(catalogId, Field(order, "text"), items);
    }

    public int? ProductId(WhatsAppCloudOptions options, Item item)
        => options.CatalogProductIds.TryGetValue(CatalogId, out var catalog)
            && catalog.TryGetValue(item.RetailerId, out var id) && id > 0 ? id : null;

    public string Format(WhatsAppCloudOptions options, IReadOnlyDictionary<int, string> names)
    {
        var text = new StringBuilder("Carrito de WhatsApp");
        var shown = 0;
        foreach (var item in Items)
        {
            var name = ProductId(options, item) is { } id && names.TryGetValue(id, out var found)
                ? found : $"Artículo {item.RetailerId} (sin equivalencia)";
            var line = $"\n{item.Quantity} × {ShortLine(name, 85)}";
            if (item.UnitPrice is { } amount && item.Currency is { } currency) line += $" · {Money(amount, currency)} c/u";
            if (text.Length + line.Length > 3450) break;
            text.Append(line);
            shown++;
        }
        if (shown < Items.Count) text.Append($"\n{Items.Count - shown} líneas adicionales en el carrito.");
        var currencies = Items.Select(i => i.Currency).Distinct().ToArray();
        if (currencies.Length == 1 && currencies[0] is { } unit && Items.All(i => i.UnitPrice.HasValue))
            text.Append($"\nTotal estimado · {Money(Items.Sum(i => i.UnitPrice!.Value * i.Quantity), unit)}");
        if (!string.IsNullOrWhiteSpace(Note)) text.Append($"\nNota · {ShortLine(Note, 450)}");
        return text.ToString();
    }

    private static string? Field(JsonElement node, string key)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(key, out var value)) return null;
        return value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null };
    }

    private static string ShortLine(string value, int length)
    {
        var single = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return single.Length <= length ? single : single[..(length - 1)] + "…";
    }

    private static string Money(decimal value, string currency)
        => $"{(currency == "COP" ? "$" : currency + " ")}{value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", Colombian)}";
}
