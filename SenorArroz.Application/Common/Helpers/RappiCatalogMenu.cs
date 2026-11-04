using System.Text.Json;
using System.Text.Json.Nodes;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

public static class RappiCatalogMenu
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ProductSku(string sku) =>
        sku.StartsWith("size-", StringComparison.OrdinalIgnoreCase) ? sku[5..] :
        sku.StartsWith("addon-", StringComparison.OrdinalIgnoreCase) ? sku[6..] : sku;

    public static RappiMenuRequest? Read(string payload)
    {
        var menu = IntegralNumbers(JsonNode.Parse(payload))?.Deserialize<RappiMenuRequest>(JsonOptions);
        if (menu?.Items is null) return null;
        if (menu.Items.Any(x => x is null)) throw new JsonException("Producto de menú inválido.");
        return menu with { Items = menu.Items.Select(x => x with { Children = x.Children ?? [] }).ToList() };
    }

    private static JsonNode? IntegralNumbers(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, value) in obj.ToList())
            {
                var normalized = IntegralNumbers(value);
                if (!ReferenceEquals(normalized, value)) obj[key] = normalized;
            }
        }
        else if (node is JsonArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var normalized = IntegralNumbers(array[index]);
                if (!ReferenceEquals(normalized, array[index])) array[index] = normalized;
            }
        }
        else if (node is JsonValue jsonValue && jsonValue.TryGetValue<JsonElement>(out var element)
            && element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number)
            && number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue)
            return JsonValue.Create((int)number);
        return node;
    }

    public static string Snapshot(RappiMenuRequest menu, IEnumerable<DeliveryAppProductMapping> mappings)
    {
        var json = JsonSerializer.Serialize(menu, JsonOptions);
        if (!menu.Items.Any(x => x.Children.Count > 0)) return json;
        var document = JsonNode.Parse(json)!.AsObject();
        document["posMappings"] = JsonSerializer.SerializeToNode(mappings.Where(x => x.IsSelected).Select(x => new
        {
            x.Sku,
            Name = x.OverrideName ?? x.Product.Name,
            Description = x.OverrideDescription ?? x.Product.CommercialProfile?.Description,
            ImageUrl = x.OverrideImageUrl ?? x.Product.CommercialProfile?.PhotoUrl,
            Price = x.OverridePrice ?? x.Product.Price
        }), JsonOptions);
        return document.ToJsonString(JsonOptions);
    }

    public static bool Matches(RappiMenuRequest expected, RappiMenuRequest actual)
    {
        if (actual.Items.Any(x => (x.Children ?? []).Any(c => c.Children is { Count: > 0 }))) return false;
        string Canonical(RappiMenuRequest menu)
        {
            var nodes = menu.Items.SelectMany(root => new[] { (Parent: "", Item: root) }
                .Concat((root.Children ?? []).Select(child => (Parent: root.Sku, Item: child))))
                .OrderBy(x => x.Parent, StringComparer.Ordinal).ThenBy(x => x.Item.Sku, StringComparer.Ordinal)
                .Select(x => new
                {
                    x.Parent, x.Item.Sku, Type = x.Item.Type.ToUpperInvariant(), x.Item.Name,
                    Description = x.Item.Description ?? "", Image = x.Item.ImageUrl ?? "", x.Item.Price,
                    x.Item.SortingPosition, x.Item.Category,
                    Limit = x.Parent.Length == 0 ? null : x.Item.MaxLimit
                });
            return JsonSerializer.Serialize(nodes, JsonOptions);
        }
        return expected.StoreId == actual.StoreId && Canonical(expected) == Canonical(actual);
    }

    public static IReadOnlyList<string> Validate(RappiMenuRequest menu, IEnumerable<DeliveryAppProductMapping> selected)
    {
        var errors = new List<string>();
        var mappings = selected.Where(x => x.IsSelected).ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (menu.Items is not { Count: > 0 and <= 1000 }) return ["El menú debe contener entre 1 y 1000 productos."];
        if (menu.Items.Any(x => x is null || x.Category is null || string.IsNullOrWhiteSpace(x.Sku)
                || x.Children is null || x.Children.Any(c => c is null || c.Category is null || string.IsNullOrWhiteSpace(c.Sku))))
            return ["El menú contiene productos, opciones o categorías incompletos."];
        if (menu.Items.Select(x => x.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != menu.Items.Count)
            errors.Add("El menú contiene productos duplicados.");
        foreach (var item in menu.Items)
        {
            if (!mappings.TryGetValue(item.Sku, out var mapping))
            {
                errors.Add($"{item.Name}: SKU sin asociación.");
                continue;
            }
            referenced.Add(mapping.Sku);
            if (item.Type != "PRODUCT" || item.Price <= 0 || item.Price != (mapping.OverridePrice ?? mapping.Product.Price)
                || string.IsNullOrWhiteSpace(item.Description) || !ValidImage(item.ImageUrl)
                || string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Category?.Name))
                errors.Add($"{item.Name}: producto, precio, descripción, categoría o imagen inválidos.");
            if (item.Children is null || item.Children.Count > 100)
            {
                errors.Add($"{item.Name}: opciones inválidas.");
                continue;
            }
            if (item.Children.Select(x => x.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != item.Children.Count)
                errors.Add($"{item.Name}: opciones duplicadas.");
            var sizes = item.Children.Where(x => x.Sku.StartsWith("size-", StringComparison.OrdinalIgnoreCase)).ToList();
            if (sizes.Count > 0 && (sizes.Select(x => x.Category.Id).Distinct().Count() != 1
                || sizes.Any(x => x.Category.MinQty != 1 || x.Category.MaxQty != 1 || x.MaxLimit != 1)))
                errors.Add($"{item.Name}: el tamaño debe permitir exactamente una elección.");
            foreach (var child in item.Children)
            {
                var sku = ProductSku(child.Sku);
                if (sku == child.Sku || !mappings.TryGetValue(sku, out var childMapping))
                {
                    errors.Add($"{item.Name}: opción sin asociación.");
                    continue;
                }
                referenced.Add(childMapping.Sku);
                if (child.Type != "TOPPING" || child.Price < 0 || child.Children is not { Count: 0 }
                    || child.MaxLimit is not > 0 || string.IsNullOrWhiteSpace(child.Name)
                    || string.IsNullOrWhiteSpace(child.Category?.Id) || string.IsNullOrWhiteSpace(child.Category.Name)
                    || child.Category.MinQty < 0 || child.Category.MaxQty < child.Category.MinQty
                    || child.Category.MaxQty <= 0 || !ValidImage(child.ImageUrl))
                    errors.Add($"{item.Name}: opción o límites inválidos.");
                if (child.Sku.StartsWith("size-", StringComparison.OrdinalIgnoreCase)
                    && (long)item.Price + child.Price != (childMapping.OverridePrice ?? childMapping.Product.Price))
                    errors.Add($"{item.Name}: el precio del tamaño no coincide con su producto asociado.");
            }
            foreach (var group in item.Children.GroupBy(x => x.Category.Id))
                if (group.Select(x => (x.Category.Name, x.Category.MinQty, x.Category.MaxQty)).Distinct().Count() != 1
                    || (group.Any(x => x.Sku.StartsWith("size-", StringComparison.OrdinalIgnoreCase))
                        && group.Any(x => x.Sku.StartsWith("addon-", StringComparison.OrdinalIgnoreCase))))
                    errors.Add($"{item.Name}: grupo de opciones inconsistente.");
        }
        if (!referenced.SetEquals(mappings.Keys)) errors.Add("El menú debe incluir todos los productos seleccionados.");
        return errors.Distinct().ToList();
    }

    public static (IReadOnlyList<ExternalDeliveryOrderLine> Lines, IReadOnlyList<string> Errors) Normalize(
        RappiMenuRequest menu, IReadOnlyList<ExternalDeliveryOrderLine> lines)
    {
        var roots = menu.Items.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
        var output = new List<ExternalDeliveryOrderLine>();
        var errors = new List<string>();
        foreach (var line in lines)
        {
            if (!roots.TryGetValue(line.Sku, out var root))
            {
                errors.Add($"{line.Name}: no pertenece al menú aprobado.");
                output.Add(line);
                continue;
            }
            var children = line.Subitems ?? [];
            var choices = root.Children.ToDictionary(x => x.Sku, StringComparer.OrdinalIgnoreCase);
            if (line.UnitPrice != root.Price) errors.Add($"{line.Name}: precio base distinto al menú aprobado.");
            if (children.Select(x => x.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Count() != children.Count)
                errors.Add($"{line.Name}: opciones duplicadas.");
            foreach (var child in children)
                if (!choices.TryGetValue(child.Sku, out var choice)
                    || !(string.Equals(child.ItemType, "TOPPING", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(child.ItemType, "PRODUCT", StringComparison.OrdinalIgnoreCase))
                    || child.UnitPrice != choice.Price || child.Quantity <= 0 || child.Quantity > choice.MaxLimit
                    || child.Subitems is { Count: > 0 })
                    errors.Add($"{line.Name}: opción, precio o cantidad distintos al menú aprobado.");
            foreach (var group in root.Children.GroupBy(x => x.Category.Id))
            {
                var skus = group.Select(x => x.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var quantity = children.Where(x => skus.Contains(x.Sku)).Sum(x => (long)x.Quantity);
                if (quantity < group.First().Category.MinQty || quantity > group.First().Category.MaxQty)
                    errors.Add($"{line.Name}: selección fuera de los límites de {group.First().Category.Name}.");
            }
            var size = children.Where(x => choices.ContainsKey(x.Sku)
                && x.Sku.StartsWith("size-", StringComparison.OrdinalIgnoreCase)).ToList();
            try
            {
                var selectedSize = size.Count == 1 ? size[0] : null;
                var unitPrice = checked(root.Price + (selectedSize?.UnitPrice ?? 0));
                output.Add(line with
                {
                    Sku = selectedSize is null ? root.Sku : ProductSku(selectedSize.Sku),
                    UnitPrice = unitPrice, ApprovedUnitPrice = unitPrice, Subitems = null
                });
                foreach (var child in children.Where(x => choices.ContainsKey(x.Sku)
                             && x.Sku.StartsWith("addon-", StringComparison.OrdinalIgnoreCase)))
                    output.Add(child with
                    {
                        Sku = ProductSku(child.Sku), ItemType = "PRODUCT", Quantity = checked(line.Quantity * child.Quantity),
                        Notes = string.Join(" ", new[] { line.Notes, child.Notes }.Where(x => !string.IsNullOrWhiteSpace(x))),
                        ApprovedUnitPrice = choices[child.Sku].Price, Subitems = null
                    });
            }
            catch (OverflowException)
            {
                errors.Add($"{line.Name}: importe o cantidad fuera de rango.");
            }
        }
        return (output, errors);
    }

    public static (IReadOnlyList<string> TurnOn, IReadOnlyList<string> TurnOff) Availability(
        RappiMenuRequest menu, IReadOnlyDictionary<string, bool> desired, ISet<string> changed)
    {
        var targets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in menu.Items)
        {
            var sizes = root.Children.Where(x => x.Sku.StartsWith("size-", StringComparison.OrdinalIgnoreCase)).ToList();
            if (sizes.Count > 0)
            {
                if (sizes.Any(x => changed.Contains(ProductSku(x.Sku))))
                    targets[root.Sku] = sizes.Any(x => desired.TryGetValue(ProductSku(x.Sku), out var available) && available);
            }
            else if (changed.Contains(root.Sku))
                targets[root.Sku] = desired.TryGetValue(root.Sku, out var available) && available;
            foreach (var child in root.Children.Where(x => changed.Contains(ProductSku(x.Sku))))
                targets[child.Sku] = desired.TryGetValue(ProductSku(child.Sku), out var available) && available;
        }
        return (targets.Where(x => x.Value).Select(x => x.Key).ToList(),
            targets.Where(x => !x.Value).Select(x => x.Key).ToList());
    }

    private static bool ValidImage(string? image) => string.IsNullOrWhiteSpace(image)
        || Uri.TryCreate(image, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
}
