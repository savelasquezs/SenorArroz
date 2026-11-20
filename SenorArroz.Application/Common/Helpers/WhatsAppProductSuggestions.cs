using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Common.Helpers;

public static class WhatsAppProductSuggestions
{
    public sealed record CatalogProduct(int Id, string Name, string? Family, string? Role, string? Presentation,
        int? ServesMin, int? ServesMax, int Price, int? GroupId = null);
    public sealed record ChoiceOffer(string Name, CatalogProduct[] Products, bool ChicharronVariants = false);
    public sealed record Match(CatalogProduct? Product, int Quantity, string? Reply, bool Clarification = false, ChoiceOffer? Offer = null);

    public static IQueryable<CatalogProduct> Catalog(IQueryable<Product> products) =>
        products.AsNoTracking().Where(p => p.Active)
        .Select(p => new CatalogProduct(p.Id, p.Name, p.CommercialProfile == null ? null : p.CommercialProfile.Name,
            p.Category.StorefrontRole, p.StorefrontVariantLabel, p.ServesPeopleMin, p.ServesPeopleMax, p.Price, p.CommercialProfileId));

    private const string Sizes = @"personal(?:es)?|duos?|trios?|familiares|familiar|super(?:\s*familiar(?:es)?)?s?";
    private const string Counts = @"[0-9]+|un|una|uno|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez";
    private const string CapacityRange = $@"(?<min>{Counts})\b(?:\s+(?:a|hasta)\s+(?<max>{Counts})\b)?";
    private const string Capacity = $@"\b(?:de\s+{CapacityRange}\s+(?:personas?|porciones?)\b|para\s+{CapacityRange}(?:\s+(?:personas?|porciones?)\b|(?=\s*[.!?]*$)))";
    private const string Correction = @"\b(?:no\s+(?:me\s+)?(?:quiero|manda|mandes|necesito|agregues|pongas|es|era)|mejor|en vez|cambi\w*|quit\w*|cancel\w*|corrij\w*|perdon|reemplaz\w*|ya no|ni|solo era|en lugar|era|sin adiciones|sin papas|sin yuca)\b";
    private static readonly Regex BeverageVolume = new(@"\b\d+ml\b");
    private static readonly CultureInfo Colombian = CultureInfo.GetCultureInfo("es-CO");

    public static IReadOnlyList<Match> Find(string text, IReadOnlyList<CatalogProduct> catalog)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4096) return [];
        var normalized = Normalize(text);
        if (Regex.IsMatch(normalized, Correction))
            return [];

        var priceQuestion = Regex.IsMatch(normalized, @"\b(?:cuanto|cuesta\w*|valen?|precio(?:s)?|que valor|cotiz\w*)\b");
        if (!priceQuestion && (text.Contains('?') || text.Contains('¿')
            || Regex.IsMatch(normalized, @"\b(?:tienen|tienes|hay|ingredientes|trae|contiene|opciones|recomiend\w*)\b"))) return [];

        var exact = catalog.Where(p => Contains(normalized, Normalize(p.Name))).ToArray();
        var fragments = exact.Length == 1 && !Regex.IsMatch(normalized, @"[,;\n+]")
            && Regex.Matches(normalized, @"\by\b").Count == Regex.Matches(Normalize(exact[0].Name), @"\by\b").Count
            ? new[] { normalized }
            : Regex.Split(Regex.Replace(normalized, @"\bcon\s+y\s+sin\b", "con o sin"), @"(?<!\d),|,(?!\d)|[;\n+]+|\s+y\s+");

        var families = catalog.GroupBy(FamilyOf).Where(group => group.Key.Length > 0)
            .OrderByDescending(group => group.Key.Length).ToArray();
        var matches = new List<Match>();
        foreach (var fragment in fragments)
        {
            var match = FindFragment(fragment.Trim(), families, priceQuestion);
            if (match is not null) matches.Add(match);
        }
        return matches.GroupBy(m => (m.Product?.Id, m.Reply, m.Clarification,
            Offer: m.Offer is null ? null : string.Join(",", m.Offer.Products.Select(p => p.Id)))).Select(group =>
        {
            var first = group.First();
            if (first.Reply is not null) return first;
            var total = group.Sum(m => (long)m.Quantity);
            return total <= 999 ? first with { Quantity = (int)total } : Clarify("¿Cuántas unidades quieres?");
        }).ToArray();
    }

    public static (string? Family, string? Size) ChicharronContext(string? message, IEnumerable<CatalogProduct> catalog)
    {
        if (string.IsNullOrWhiteSpace(message)) return (null, null);
        var text = Normalize(message);
        if (Regex.IsMatch(text, Correction)) return (null, null);
        var families = catalog.Where(x => x.Role == "rice").Select(FamilyOf).Distinct()
            .Where(x => x.Length > 0 && Contains(text, x)).ToArray();
        if (families.Length > 1 || families.Length == 1 && families[0] is not ("paisa" or "ropa vieja"))
            return (null, null);
        var sizes = MentionedSizes(text);
        return (families.SingleOrDefault(), sizes.Length == 1 ? sizes[0] : null);
    }

    public static (string Family, string Size, bool WithChicharron)? ChicharronPresentation(CatalogProduct product)
    {
        if (product.Role != "rice") return null;
        var family = FamilyOf(product);
        if (family is not ("paisa" or "ropa vieja")) return null;
        var sizes = ProductSizes(product);
        return sizes.Length == 1 ? (family, sizes[0], WithChicharron(product)) : null;
    }

    private static string[] MentionedSizes(string text) =>
        Regex.Matches(text, $@"\b(?:{Sizes})\b").Select(m => CanonicalSize(m.Value)).Distinct().ToArray();

    private static string[] ProductSizes(CatalogProduct product) => MentionedSizes(Normalize(product.Presentation + " " + product.Name));

    private static Match? FindFragment(string text, IReadOnlyList<IGrouping<string, CatalogProduct>> catalog, bool priceQuestion)
    {
        if (text.Length == 0) return null;
        var families = catalog.Where(group => Contains(text, group.Key)).ToArray();
        if (families.Length == 0) return null;

        var family = families[0];
        if (families.Skip(1).Any(other => !Contains(family.Key, other.Key))) return null;
        var products = family.ToArray();
        var remaining = ReplaceTerm(text, family.Key);
        ChoiceOffer? offer = null;
        var rice = products.Any(p => p.Role == "rice");
        if (rice)
        {
            var variants = products.Select(WithChicharron).Distinct().ToArray();
            if (variants.Length > 1)
            {
                var with = Regex.IsMatch(text, @"\bcon\s+chich\w*\b");
                var without = Regex.IsMatch(text, @"\bsin\s+chich\w*\b");
                if (with != without && !Regex.IsMatch(text, @"\bcon\s+o\s+sin\b"))
                    products = products.Where(p => WithChicharron(p) == with).ToArray();
            }
            else if ((Regex.IsMatch(text, @"\bcon\s+chich\w*\b") && !variants[0])
                || (Regex.IsMatch(text, @"\bsin\s+chich\w*\b") && variants[0]))
            {
                return Clarify("¿Me confirmas la variante que prefieres?");
            }

            remaining = Regex.Replace(remaining, @"\b(?:(?:con\s+o\s+sin|con|sin)\s+chich\w*)\b", " ");
            var sizes = MentionedSizes(remaining);
            if (sizes.Length > 1) return Clarify("¿Qué tamaño prefieres?");
            var size = sizes.SingleOrDefault();
            if (size is not null)
                remaining = Regex.Replace(remaining, $@"\b(?:{Sizes})\b", " ");

            var capacity = Regex.Match(remaining, Capacity);
            if (capacity.Success)
            {
                var min = Number(capacity.Groups["min"].Value);
                var max = capacity.Groups["max"].Success ? Number(capacity.Groups["max"].Value) : min;
                if (min is not > 0 || max is not > 0 || max < min) return Clarify("¿Para cuántas personas lo necesitas?");
                if (size is null)
                {
                    size = min == max ? min switch { 1 => "personal", 2 => "duo", 3 => "trio", _ => null } : null;
                    if (size is null)
                        products = products.Where(p => p.ServesMin <= min && p.ServesMax >= max).ToArray();
                }
                remaining = remaining.Remove(capacity.Index, capacity.Length).Insert(capacity.Index, " ");
            }
            if (size is not null)
                products = products.Where(p => ProductSizes(p).Contains(size)).ToArray();
            if (sizes.Length == 0 && !capacity.Success)
                return Clarify(variants.Length > 1 && products.Select(WithChicharron).Distinct().Count() > 1
                    ? "¿Qué tamaño prefieres y lo quieres con o sin chicharrón?" : "¿Qué tamaño prefieres?");
            if (products.Length == 0)
                return Clarify("¿Qué tamaño prefieres?");
            if (products.Length > 1)
            {
                var name = Colombian.TextInfo.ToTitleCase(family.Key);
                var offeredSizes = products.SelectMany(ProductSizes).Distinct().ToArray();
                if (offeredSizes.Length == 1) name += $" · {SizeLabel(offeredSizes[0])}";
                var options = products.OrderBy(p => p.ServesMax).ThenBy(WithChicharron).ThenBy(p => p.Id).ToArray();
                offer = new(name, options, options.Select(WithChicharron).Distinct().Count() > 1);
            }
        }
        else if (family.Key is "papas" or "yuca")
        {
            var packs = Regex.Matches(remaining, family.Key == "yuca"
                ? @"\bx\s*(?<size>\d+)\b|\b(?<size>\d+)\s+unidades?\b"
                : @"\b(?<size>\d+)\s*(?:g|gr|grs|gramos?)\b").ToArray();
            if (packs.Length > 1) return Clarify("¿Qué presentación prefieres?");
            if (packs.Length == 0)
            {
                var name = family.Key == "yuca" ? "Yucas" : "Papas";
                offer = new(name, products.OrderBy(p => p.Price).ThenBy(p => p.Id).ToArray());
            }
            else
            {
                var size = packs[0].Groups["size"].Value;
                products = products.Where(p => Regex.IsMatch(Normalize(p.Presentation ?? p.Name),
                    $@"(?<!\d){Regex.Escape(size)}(?!\d)")).ToArray();
                remaining = remaining.Remove(packs[0].Index, packs[0].Length).Insert(packs[0].Index, " ");
            }
        }
        else if (products.All(p => p.Role == "beverage"))
        {
            var (productMatch, quantityText) = MatchBeverage(family.Key, remaining, products);
            if (productMatch is null) return Clarify("¿Qué presentación de la bebida prefieres?");
            products = [productMatch];
            remaining = quantityText;
        }
        else
        {
            products = products.Where(p => Contains(text, Normalize(p.Name))).ToArray();
        }

        if (offer is null && products.Length != 1) return Clarify("¿Qué presentación prefieres?");
        if (priceQuestion)
        {
            if (offer is not null)
                return new(null, 1, string.Join("\n", offer.Products.Select(p => $"{p.Name} · $ {p.Price.ToString("N0", Colombian)}")));
            var product = products[0];
            return new(product, 1, $"{product.Name} cuesta $ {product.Price.ToString("N0", Colombian)}.");
        }
        if (Regex.IsMatch(remaining, @"\b(?:sin|extra|aparte|agregale|quitale)\b")) return null;
        remaining = Regex.Replace(remaining, Capacity, " ");
        remaining = Regex.Replace(remaining, $@"\b(?:{Counts})\s+(?:minutos?|horas?|am|pm)\b", " ");
        if (Regex.IsMatch(remaining, @"\b(?:calle|carrera|diagonal|transversal|direccion)\b")) return null;
        if (Regex.IsMatch(remaining, @"(?:^|\s)[+-]\s*\d|\d[.,/]\d|\bmedia?\b|\b(?:o|entre|aprox\w*|varias?|varios)\b"))
            return Clarify("¿Me confirmas la cantidad que quieres?");
        var quantities = Regex.Matches(remaining, $@"\b(?:{Counts})\b").Select(m => Number(m.Value)).ToArray();
        if (quantities.Length > 1 || (quantities.Length == 1 && quantities[0] is not > 0))
            return Clarify("¿Me confirmas la cantidad que quieres?");
        return new(offer is null ? products[0] : null, quantities.Length == 1 ? quantities[0]!.Value : 1, null, Offer: offer);
    }

    private static (CatalogProduct? Product, string Remaining) MatchBeverage(
        string family, string text, IReadOnlyList<CatalogProduct> products)
    {
        text = NormalizeBeverageVariant(text);
        var volumes = BeverageVolume.Matches(text).Select(match => match.Value).Distinct().ToArray();
        if (volumes.Length > 1) return (null, text);
        var volume = volumes.SingleOrDefault();
        var zero = Contains(text, "zero");
        var remaining = BeverageVolume.Replace(ReplaceTerm(text, "zero"), " ");

        var presentations = products.Select(product =>
        {
            var name = NormalizeBeverageVariant(ReplaceTerm(Normalize(product.Name), family));
            var presentation = NormalizeBeverageVariant(Normalize(product.Presentation ?? ""));
            var description = Regex.Replace(BeverageVolume.Replace(ReplaceTerm(name, "zero"), " "), @"\s+", " ").Trim();
            return (Product: product, Detail: name + " " + presentation, Description: description);
        }).ToArray();
        var described = presentations.Where(choice => choice.Description.Length > 0 && Contains(remaining, choice.Description)).ToArray();
        var choices = (described.Length > 0 ? described : presentations).Where(choice => Contains(choice.Detail, "zero") == zero
            && (volume is null || Contains(choice.Detail, volume))).ToArray();
        if (choices.Length != 1 || volume is null && described.Length == 0
            && (products.Count > 1 || BeverageVolume.IsMatch(choices[0].Detail))) return (null, text);

        var selected = choices[0];
        if (described.Length > 0) remaining = ReplaceTerm(remaining, selected.Description);
        return (selected.Product, remaining);
    }

    private static string FamilyOf(CatalogProduct product)
    {
        var name = Normalize(product.Family ?? product.Name);
        if (product.Role == "rice")
        {
            var productName = Normalize(product.Name);
            if (Contains(productName, "ropa vieja")) return "ropa vieja";
            if (Contains(productName, "paisa")) return "paisa";
            return Regex.Replace(Regex.Replace(name, @"\b(?:arroz|(?:con|sin)\s+chich\w*)\b", " "),
                $@"\b(?:{Sizes})\b", " ").Trim();
        }
        if (product.Role == "addition" && Contains(name, "papas")) return "papas";
        if (product.Role == "addition" && Contains(name, "yuca")) return "yuca";
        if (product.Role == "beverage") return product.Family is null
            ? BeverageVolume.Replace(ReplaceTerm(NormalizeBeverageVariant(name), "zero"), " ").Trim() : name;
        return Normalize(product.Name);
    }

    private static bool WithChicharron(CatalogProduct p) => Regex.IsMatch(Normalize(p.Name), @"\bcon\s+chich\w*\b");
    private static Match Clarify(string reply) => new(null, 0, reply, true);
    private static bool Contains(string text, string term) => Regex.IsMatch(text, TermPattern(term));
    private static string ReplaceTerm(string text, string term) => Regex.Replace(text, TermPattern(term), " ");
    private static string TermPattern(string term) => $@"(?<![\p{{L}}\d]){Regex.Escape(term)}(?![\p{{L}}\d])";
    private static string NormalizeBeverageVariant(string text) => Regex.Replace(text, @"\b(?:sin\s+azucar|cero)\b", "zero");

    private static string CanonicalSize(string size) => size.StartsWith("super", StringComparison.Ordinal) ? "super"
        : size == "familiares" ? "familiar" : size == "personales" ? "personal" : size.TrimEnd('s');

    public static string SizeLabel(string size) => size switch
    {
        "super" => "Súper",
        "duo" => "Dúo",
        "trio" => "Trío",
        _ => Colombian.TextInfo.ToTitleCase(size)
    };

    private static int? Number(string value) => value switch
    {
        "un" or "una" or "uno" => 1, "dos" => 2, "tres" => 3, "cuatro" => 4, "cinco" => 5,
        "seis" => 6, "siete" => 7, "ocho" => 8, "nueve" => 9, "diez" => 10,
        _ => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : null
    };

    private static string Normalize(string value)
    {
        // Las equivalencias léxicas proceden de WhatsAppProductSearch y WhatsAppLabReplies.
        var text = new string(value.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        text = Regex.Replace(text, @"\b(?:ropa|roa)[\s.]+(?:v(?:ie|i)ja|viejero|viejo|biela)\b|\b(?:trapo|mecha)\s+[vb]iej[oa]\b|\bropavieja\b|\bvieja\s+ropa\b", "ropa vieja");
        text = Regex.Replace(text, @"\bpaiza\b", "paisa");
        text = Regex.Replace(text, @"\b(?:cabornara|carbonera|carbonada)\b", "carbonara");
        text = Regex.Replace(text, @"\bfamilar\b", "familiar");
        text = Regex.Replace(text, @"\b(con|sin)\s+chillaron\b", "$1 chicharron");
        text = Regex.Replace(text, @"\bcom(?=\s+chich\w*)", "con");
        text = Regex.Replace(text, @"\bpapitas?(?:\s+fritas)?\b|\bpapas?\s+fritas\b|\bpapas?\b", "papas");
        text = Regex.Replace(text, @"\b(?:yucas?|yu(?:qu|k|gu)itas?)(?:\s+fritas)?\b", "yuca");
        text = Regex.Replace(text, @"\b(\d+)\s*(?:g|gr|grs|gramos?)\b", "$1 g");
        text = Regex.Replace(text, @"(?<=\d)(?=(?:personas?|porciones?|unidades?)\b)", " ");
        text = Regex.Replace(text, @"\bcoca[\s-]*cola\b", "cocacola");
        text = Regex.Replace(text, @"\b(?:(?:1|un)\s+)?litro\s+y\s+medio\b", "1500ml");
        text = Regex.Replace(text, @"\bmedio\s+litro\b", "500ml");
        text = Regex.Replace(text, @"\bun\s+litro\b", "1000ml");
        text = Regex.Replace(text, @"\b(?<amount>\d+(?:[.,]\d{1,3})?)\s*(?<unit>mililitros?|ml|litros?|lts?|l)\b", match =>
        {
            if (!decimal.TryParse(match.Groups["amount"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount)) return match.Value;
            var milliliters = match.Groups["unit"].Value.StartsWith('l') ? amount * 1000 : amount;
            return milliliters is > 0 and <= int.MaxValue && milliliters == decimal.Truncate(milliliters)
                ? $"{(int)milliliters}ml" : match.Value;
        });
        text = Regex.Replace(text, @"\bpet\s*(\d{2,4})(?:\s*ml)?\b", "$1ml");
        return Regex.Replace(text, @"[ \t]+", " ").Trim();
    }
}
