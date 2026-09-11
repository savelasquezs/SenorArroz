from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]


def replace_once(relative_path: str, old: str, new: str) -> None:
    path = ROOT / relative_path
    text = path.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"Expected exactly one match in {relative_path}, found {count}: {old[:80]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8")


FLOW_SERVICE = "SenorArroz.API/Services/WhatsAppCommerceFlowService.cs"
STOREFRONT_SERVICE = "SenorArroz.API/Services/StorefrontCommerceService.cs"
STOREFRONT_CONTROLLER = "SenorArroz.API/Controllers/PublicStorefrontController.cs"
FLOW_JSON = "SenorArroz.API/WhatsAppFlows/storefront-flow.json"
FLOW_TESTS = "SenorArroz.Tests/WhatsAppFlowSecurityTests.cs"
STOREFRONT_TESTS = "SenorArroz.Tests/PublicStorefrontControllerTests.cs"

# 1) Cross-selling: a recommendation is already a concrete ProductId, so add it directly.
replace_once(
    FLOW_SERVICE,
    '''                if (command == "recommendation")
                {
                    var recommendationId = GetInt(data, "recommendation_id");
                    var match = FindProduct(catalog!, recommendationId);
                    if (match is null) error = "Elige una sugerencia disponible.";
                    else
                    {
                        state.Category = match.Value.Category;
                        state.SelectedProductGroup = match.Value.Group.Key;
                        state.PendingRecommendationProductId = recommendationId;
                        state.EditingProductId = null;
                        state.CartMode = "variant";
                        next = "CART";
                    }
                    break;
                }
''',
    '''                if (command == "recommendation")
                {
                    var recommendationId = GetInt(data, "recommendation_id");
                    var recommendation = StorefrontRecommendationSelector.Select(catalog!, state.Cart, 3)
                        .FirstOrDefault(x => x.Option.ProductId == recommendationId);
                    if (recommendation is null || recommendation.Option.AvailabilityStatus == "unavailable")
                        error = "Elige una sugerencia disponible.";
                    else if (!AddOrReplace(state.Cart, recommendation.Option.ProductId, 1))
                        error = "El carrito admite máximo 30 productos distintos.";
                    else
                    {
                        TrackEvent(session, "recommendation_added", session.BranchId, current,
                            recommendation.Option.ProductId.ToString(CultureInfo.InvariantCulture));
                        state.Category = null;
                        state.SelectedProductGroup = null;
                        state.PendingRecommendationProductId = null;
                        state.EditingProductId = null;
                        state.CartMode = "summary";
                        InvalidateQuote(state);
                        next = "CART";
                    }
                    break;
                }
''')

# 2) Preserve an explicit "none" choice when there are multiple benefits.
replace_once(
    FLOW_SERVICE,
    '''            case "BENEFITS":
                state.BenefitSelection = GetString(data, "benefit_selection");
                if (state.BenefitSelection == "none") state.BenefitSelection = null;
                InvalidateQuote(state);
                var benefitQuote = await GetQuoteAsync(session, state, ct);
                if (!benefitQuote.Success)
                    return await HandleQuoteFailureAsync(session, state, benefitQuote, ct);
                if (benefitQuote.Quote!.BenefitConflict)
                    error = "Elige solo un beneficio para continuar.";
                else next = "PAYMENT";
                break;
''',
    '''            case "BENEFITS":
                state.BenefitSelection = GetString(data, "benefit_selection");
                InvalidateQuote(state);
                var benefitQuote = await GetQuoteAsync(session, state, ct);
                if (!benefitQuote.Success)
                    return await HandleQuoteFailureAsync(session, state, benefitQuote, ct);
                if (benefitQuote.Quote!.BenefitConflict && state.BenefitSelection != "none")
                    error = "Elige solo un beneficio para continuar.";
                else next = "PAYMENT";
                break;
''')

# 3) If there is exactly one applicable benefit, select it server-side and skip BENEFITS.
replace_once(
    FLOW_SERVICE,
    '''        ApplyQuoteState(session, state, quote.Quote!);
        return (quote.Quote!.AvailableBenefits.Count > 0 ? "BENEFITS" : "PAYMENT", null);
''',
    '''        ApplyQuoteState(session, state, quote.Quote!);
        var availableBenefits = quote.Quote!.AvailableBenefits;
        if (availableBenefits.Count == 1)
        {
            state.BenefitSelection = availableBenefits.Single().Source;
            InvalidateQuote(state);
            return ("PAYMENT", null);
        }
        state.BenefitSelection = null;
        return (availableBenefits.Count > 1 ? "BENEFITS" : "PAYMENT", null);
''')

# 4) Friendly $0 display and defensive repair of common UTF-8/Latin-1 mojibake in catalog text.
replace_once(
    FLOW_SERVICE,
    '''        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var available = group.Options.Where(x => x.AvailabilityStatus != "unavailable").ToArray();
            var row = new Dictionary<string, object?>
            {
                ["id"] = group.Key,
                ["title"] = ShortTitle(group.Name),
                ["description"] = available.Length == 0 ? "Agotado" : $"Desde {Money(available.Min(x => x.Price))}",
                ["enabled"] = available.Length > 0
            };
''',
    '''        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var available = group.Options.Where(x => x.AvailabilityStatus != "unavailable").ToArray();
            var minimumPrice = available.Length == 0 ? 0 : available.Min(x => x.Price);
            var row = new Dictionary<string, object?>
            {
                ["id"] = group.Key,
                ["title"] = ShortTitle(group.Name),
                ["description"] = available.Length == 0 ? "Agotado" : minimumPrice <= 0 ? "Gratis" : $"Desde {Money(minimumPrice)}",
                ["enabled"] = available.Length > 0
            };
''')

replace_once(
    FLOW_SERVICE,
    '''                title = ShortTitle($"{item.Quantity} × {value.Option?.Name ?? "Producto no disponible"}"),
                description = value.Option is null ? "Ya no está disponible" : $"{value.Option.VariantLabel} · {Money(value.Option.Price * item.Quantity)}"
''',
    '''                title = ShortTitle($"{item.Quantity} × {value.Option?.Name ?? "Producto no disponible"}"),
                description = value.Option is null
                    ? "Ya no está disponible"
                    : ShortDescription($"{value.Option.VariantLabel} · {PriceLabel(value.Option.Price * item.Quantity)}")
''')

replace_once(
    FLOW_SERVICE,
    '''            id = x.Option.ProductId.ToString(CultureInfo.InvariantCulture),
            title = ShortTitle(x.Group.Name),
            description = $"{x.Option.VariantLabel} · {Money(x.Option.Price)}"
''',
    '''            id = x.Option.ProductId.ToString(CultureInfo.InvariantCulture),
            title = ShortTitle(x.Group.Name),
            description = ShortDescription($"{x.Option.VariantLabel} · {PriceLabel(x.Option.Price)}")
''')

replace_once(
    FLOW_SERVICE,
    '''    private static string BuildSummary(PublicDeliveryQuoteDto quote, string? paymentMethod)
    {
        var lines = string.Join("\\n", quote.Items.Select(x => $"{x.Quantity} × {x.Name}: {Money(x.Subtotal)}"));
        var discount = quote.DiscountTotal > 0 ? $"\\nDescuentos: -{Money(quote.DiscountTotal)}" : string.Empty;
        var delivery = quote.FulfillmentType == "delivery" ? $"\\nDomicilio: {Money(quote.EstimatedDeliveryFee)}" : string.Empty;
        return $"{BuildAddressSummary(quote)}\\n\\n{lines}\\nSubtotal: {Money(quote.Subtotal)}{discount}{delivery}\\nTotal: {Money(quote.Total)}\\nPago: {(paymentMethod == "online" ? "Wompi" : "Efectivo")}";
    }
''',
    '''    private static string BuildSummary(PublicDeliveryQuoteDto quote, string? paymentMethod)
    {
        var lines = string.Join("\\n", quote.Items.Select(x =>
            $"{x.Quantity} × {FixDisplayEncoding(x.Name)}: {PriceLabel(x.Subtotal)}"));
        var discount = quote.DiscountTotal > 0 ? $"\\nDescuentos: -{Money(quote.DiscountTotal)}" : string.Empty;
        var benefit = quote.AppliedBenefit is null
            ? string.Empty
            : $"\\nBeneficio: {FixDisplayEncoding(quote.AppliedBenefit.Title)}";
        var delivery = string.Empty;
        if (quote.FulfillmentType == "delivery")
        {
            var originalDeliveryFee = quote.Branches.FirstOrDefault(x => x.Id == quote.CheckoutBranchId)?.EstimatedDeliveryFee
                ?? quote.EstimatedDeliveryFee;
            delivery = originalDeliveryFee > 0 && quote.EstimatedDeliveryFee == 0
                ? $"\\nDomicilio: Gratis (antes {Money(originalDeliveryFee)})"
                : $"\\nDomicilio: {PriceLabel(quote.EstimatedDeliveryFee)}";
        }
        return $"{BuildAddressSummary(quote)}\\n\\n{lines}\\nSubtotal: {Money(quote.Subtotal)}{discount}{benefit}{delivery}\\nTotal: {Money(quote.Total)}\\nPago: {(paymentMethod == "online" ? "Wompi" : "Efectivo")}";
    }
''')

replace_once(
    FLOW_SERVICE,
    '''    private static string VariantDescription(string? category, PublicProductOptionDto option)
    {
        var people = category is "rice" or "combo" ? PeopleText(option.ServesPeopleMin, option.ServesPeopleMax) : string.Empty;
        var availability = option.AvailabilityStatus == "lowStock" ? " · Pocas unidades" : string.Empty;
        return ShortDescription($"{Money(option.Price)}{(string.IsNullOrEmpty(people) ? string.Empty : $" · {people}")}{availability}");
    }
''',
    '''    private static string VariantDescription(string? category, PublicProductOptionDto option)
    {
        var people = category is "rice" or "combo" ? PeopleText(option.ServesPeopleMin, option.ServesPeopleMax) : string.Empty;
        var availability = option.AvailabilityStatus == "lowStock" ? " · Pocas unidades" : string.Empty;
        return ShortDescription($"{PriceLabel(option.Price)}{(string.IsNullOrEmpty(people) ? string.Empty : $" · {people}")}{availability}");
    }
''')

replace_once(
    FLOW_SERVICE,
    '''    private static string Money(int value) => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string ShortTitle(string value) => CompactText(value, 30);
    private static string ShortDescription(string value) => CompactText(value, 300);
    private static string CompactText(string value, int maxLength)
    {
        var clean = string.Join(' ', new string(value.Where(x => !char.IsControl(x)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= maxLength ? clean : clean[..(maxLength - 1)] + "…";
    }
''',
    '''    private static string Money(int value) => value.ToString("C0", CultureInfo.GetCultureInfo("es-CO"));
    private static string PriceLabel(int value) => value <= 0 ? "Gratis" : Money(value);
    private static string ShortTitle(string value) => CompactText(value, 30);
    private static string ShortDescription(string value) => CompactText(value, 300);
    private static string CompactText(string value, int maxLength)
    {
        value = FixDisplayEncoding(value);
        var clean = string.Join(' ', new string(value.Where(x => !char.IsControl(x)).ToArray())
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= maxLength ? clean : clean[..(maxLength - 1)] + "…";
    }
    private static string FixDisplayEncoding(string value)
    {
        if (string.IsNullOrEmpty(value) || (!value.Contains('Ã') && !value.Contains('Â'))) return value;
        var decoded = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
        return decoded.Contains('�') || SuspiciousEncodingScore(decoded) >= SuspiciousEncodingScore(value)
            ? value
            : decoded;
    }
    private static int SuspiciousEncodingScore(string value) => value.Count(x => x is 'Ã' or 'Â' or '�');
''')

# 5) Make "Continuar sin beneficio" a real explicit choice in the shared storefront engine.
replace_once(
    STOREFRONT_SERVICE,
    '''        var selectedBenefit = Normalize(request.BenefitSelection);
        var appliedBenefit = availableBenefits.Count switch
        {
            0 => null,
            1 => availableBenefits[0],
            _ => availableBenefits.FirstOrDefault(x => Normalize(x.Source) == selectedBenefit),
        };
        var benefitConflict = availableBenefits.Count > 1 && appliedBenefit is null;
''',
    '''        var selectedBenefit = Normalize(request.BenefitSelection);
        var skipBenefits = selectedBenefit == "none";
        var appliedBenefit = skipBenefits
            ? null
            : availableBenefits.Count switch
            {
                0 => null,
                1 => availableBenefits[0],
                _ => availableBenefits.FirstOrDefault(x => Normalize(x.Source) == selectedBenefit),
            };
        var benefitConflict = availableBenefits.Count > 1 && appliedBenefit is null && !skipBenefits;
''')

replace_once(
    STOREFRONT_CONTROLLER,
    '''    [RegularExpression("^(daily_promotion|loyalty)?$")]
    public string? BenefitSelection { get; set; }
''',
    '''    [RegularExpression("^(daily_promotion|loyalty|none)?$")]
    public string? BenefitSelection { get; set; }
''')

# 6) Remove the duplicated fixed cart-tip caption; keep the dynamic one only.
replace_once(
    FLOW_JSON,
    '''              {
                "type": "TextCaption",
                "text": "Las sugerencias son opcionales y nunca bloquean tu pedido."
              },
''',
    '''''')

# 7) Regression coverage.
replace_once(
    FLOW_TESTS,
    '''        Assert.Contains("\\\"recommendation_id\\\":\\\"\\\"", cartJson);
        Assert.Contains("\\\"cart_command\\\":\\\"${form.command}\\\"", cartJson);
        Assert.Contains("\\\"command\\\":\\\"cart_submit\\\"", cartJson);

        var summary = screens.Single(x => x.GetProperty("id").GetString() == "SUMMARY");
''',
    '''        Assert.Contains("\\\"recommendation_id\\\":\\\"\\\"", cartJson);
        Assert.Contains("\\\"cart_command\\\":\\\"${form.command}\\\"", cartJson);
        Assert.Contains("\\\"command\\\":\\\"cart_submit\\\"", cartJson);
        var cartChildren = cart.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();
        Assert.DoesNotContain(cartChildren, x => x.TryGetProperty("text", out var text)
            && text.GetString() == "Las sugerencias son opcionales y nunca bloquean tu pedido.");
        Assert.Contains(cartChildren, x => x.TryGetProperty("text", out var text) && text.GetString() == "${data.cart_tip}");

        var summary = screens.Single(x => x.GetProperty("id").GetString() == "SUMMARY");
''')

replace_once(
    STOREFRONT_TESTS,
    '''        Assert.True(conflict.BenefitConflict);
        Assert.Equal(2, conflict.AvailableBenefits.Count);
        Assert.Null(conflict.AppliedBenefit);

        request.BenefitSelection = "loyalty";
''',
    '''        Assert.True(conflict.BenefitConflict);
        Assert.Equal(2, conflict.AvailableBenefits.Count);
        Assert.Null(conflict.AppliedBenefit);

        request.BenefitSelection = "none";
        var noneAction = await controller.Quote(request, default, auth);
        var none = Assert.IsType<ApiResponse<PublicDeliveryQuoteDto>>(Assert.IsType<OkObjectResult>(noneAction.Result).Value).Data!;
        Assert.False(none.BenefitConflict);
        Assert.Null(none.AppliedBenefit);

        request.BenefitSelection = "loyalty";
''')

# Parse the Flow after editing so malformed JSON can never be committed by this patch.
flow = json.loads((ROOT / FLOW_JSON).read_text(encoding="utf-8"))
cart = next(screen for screen in flow["screens"] if screen["id"] == "CART")
children = cart["layout"]["children"][0]["children"]
static_tip = [x for x in children if x.get("text") == "Las sugerencias son opcionales y nunca bloquean tu pedido."]
dynamic_tip = [x for x in children if x.get("text") == "${data.cart_tip}"]
if static_tip or len(dynamic_tip) != 1:
    raise SystemExit("CART tip regression: expected only one dynamic cart_tip caption")

print("WhatsApp flow polish patch applied and validated.")
