using System.Globalization;
using System.Text.Json;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

internal static class CashClosureAuditMapper
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");
    public static readonly DeliveryTrackingAlertType[] IncludedTrackingAlertTypes =
        DeliveryTrackingReviewPolicy.IncludedAlertTypes;

    public static IReadOnlyList<CashClosureAuditLogicalEvent> Consolidate(
        IReadOnlyCollection<EntityAuditLog> logs,
        IReadOnlyDictionary<int, string> productNames,
        IReadOnlyDictionary<int, IReadOnlyList<string>> orderProductNames)
    {
        var result = new List<CashClosureAuditLogicalEvent>();
        var orderModifications = logs
            .Where(IsOrderModification)
            .GroupBy(log => new
            {
                log.EntityId,
                Operation = GetOperationKey(log),
            });

        foreach (var group in orderModifications)
            result.Add(ConsolidateOrderModification(group.ToList(), productNames));

        result.AddRange(logs
            .Where(log => !IsOrderModification(log))
            .Select(log => FromSingleLog(log, productNames, orderProductNames)));

        return result
            .OrderByDescending(x => x.ChangedAt)
            .ThenByDescending(x => x.Id)
            .ToList();
    }

    public static IReadOnlyList<int> ReferencedProductIds(IEnumerable<EntityAuditLog> logs) =>
        logs.SelectMany(log => ParseDelta(log.MoneyDeltaJson).ProductChanges)
            .SelectMany(change => new[] { change.ProductIdBefore, change.ProductIdAfter })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

    public static bool ShouldIncludeInDailyEmail(CashClosureAuditLogicalEvent auditEvent)
    {
        if (string.Equals(auditEvent.EntityType, "expense_header", StringComparison.OrdinalIgnoreCase)
            && string.Equals(auditEvent.OperationType, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(auditEvent.EntityType, "order", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(auditEvent.OperationType, "cancelled", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsDetailedNetReduction(auditEvent);
    }

    public static bool ShouldIncludeInClosureAudit(CashClosureAuditLogicalEvent auditEvent) =>
        !IsOrderModification(auditEvent)
        || IsDetailedNetReduction(auditEvent);

    public static CashClosureAuditEventDto ToDto(CashClosureAuditLogicalEvent auditEvent) => new()
    {
        Id = auditEvent.Id,
        ChangedAt = auditEvent.ChangedAt,
        UserName = auditEvent.UserName,
        EntityType = auditEvent.EntityType,
        EntityId = auditEvent.EntityId,
        OperationType = auditEvent.OperationType,
        SummaryText = auditEvent.SummaryText,
        TotalBefore = auditEvent.TotalBefore,
        TotalAfter = auditEvent.TotalAfter,
        Difference = auditEvent.Difference,
    };

    public static string GroupKey(CashClosureAuditLogicalEvent auditEvent) =>
        (auditEvent.EntityType, auditEvent.OperationType) switch
        {
            ("order", "cancelled") => "orders_cancelled",
            ("order", "deleted") => "orders_deleted",
            ("order", _) => "orders_modified",
            ("expense_header", "deleted") => "expenses_deleted",
            ("expense_header", _) => "expenses_modified",
            _ => "other",
        };

    public static string GroupTitle(string key) => key switch
    {
        "orders_cancelled" => "Pedidos cancelados",
        "orders_deleted" => "Pedidos eliminados",
        "orders_modified" => "Reducciones monetarias en pedidos",
        "expenses_deleted" => "Gastos eliminados",
        "expenses_modified" => "Gastos modificados monetariamente",
        _ => "Otros",
    };

    public static AuditMoneyDelta ParseDelta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new AuditMoneyDelta();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new AuditMoneyDelta
            {
                TotalBefore = TryGetDecimal(root, "total_before"),
                TotalAfter = TryGetDecimal(root, "total_after"),
                Difference = TryGetDecimal(root, "difference"),
                ProductChanges = TryGetProductChanges(root),
            };
        }
        catch
        {
            return new AuditMoneyDelta();
        }
    }

    private static CashClosureAuditLogicalEvent ConsolidateOrderModification(
        IReadOnlyList<EntityAuditLog> logs,
        IReadOnlyDictionary<int, string> productNames)
    {
        var ordered = logs.OrderBy(x => x.Id).ToList();
        var parsed = ordered.Select(log => (Log: log, Delta: ParseDelta(log.MoneyDeltaJson))).ToList();
        var lineChanges = parsed
            .SelectMany(x => x.Delta.ProductChanges.Select(change => (x.Log.Id, Change: change)))
            .OrderBy(x => x.Id)
            .Select(x => x.Change)
            .ToList();
        var headerDeltas = parsed
            .Where(x => x.Delta.ProductChanges.Count == 0
                && x.Delta.TotalBefore.HasValue
                && x.Delta.TotalAfter.HasValue)
            .ToList();

        decimal? totalBefore;
        decimal? totalAfter;
        decimal difference;

        if (headerDeltas.Count > 0)
        {
            totalBefore = headerDeltas.First().Delta.TotalBefore;
            totalAfter = headerDeltas.Last().Delta.TotalAfter;
            difference = (totalAfter ?? 0) - (totalBefore ?? 0);
        }
        else
        {
            difference = parsed.Sum(x => x.Delta.Difference ?? 0);
            totalAfter = parsed.LastOrDefault(x => x.Delta.TotalAfter.HasValue).Delta?.TotalAfter;
            totalBefore = totalAfter.HasValue ? totalAfter.Value - difference : null;
        }

        var representative = ordered[^1];
        var userName = ActorName(representative);
        var changeSummary = FormatProductChanges(lineChanges, productNames);
        var reduction = Math.Abs(difference);
        var totalsText = totalBefore.HasValue && totalAfter.HasValue
            ? $" El valor bajó de {FormatMoney(totalBefore.Value)} a {FormatMoney(totalAfter.Value)}."
            : string.Empty;
        var summary = $"Pedido #{representative.EntityId}: {changeSummary} Merma neta: {FormatMoney(reduction)}.{totalsText}".Trim();

        return new CashClosureAuditLogicalEvent
        {
            Id = representative.Id,
            ChangedAt = representative.ChangedAt,
            UserName = userName,
            EntityType = representative.EntityType,
            EntityId = representative.EntityId,
            OperationType = representative.OperationType,
            SummaryText = summary,
            DetailText = WithActorAndColombiaTime(representative.ChangedAt, userName, summary),
            TotalBefore = totalBefore,
            TotalAfter = totalAfter,
            Difference = difference,
            HasProductChanges = lineChanges.Count > 0,
        };
    }

    private static CashClosureAuditLogicalEvent FromSingleLog(
        EntityAuditLog log,
        IReadOnlyDictionary<int, string> productNames,
        IReadOnlyDictionary<int, IReadOnlyList<string>> orderProductNames)
    {
        var delta = ParseDelta(log.MoneyDeltaJson);
        var userName = ActorName(log);
        var summary = log.SummaryText;

        if (string.Equals(log.EntityType, "order", StringComparison.OrdinalIgnoreCase)
            && string.Equals(log.OperationType, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            var products = delta.ProductChanges
                .SelectMany(change => new[] { change.ProductIdBefore, change.ProductIdAfter })
                .Where(id => id.HasValue)
                .Select(id => ProductName(id!.Value, productNames))
                .Distinct()
                .ToList();
            if (products.Count == 0 && orderProductNames.TryGetValue(log.EntityId, out var namesFromOrder))
                products.AddRange(namesFromOrder);

            var affectedTotal = delta.TotalBefore ?? Math.Abs(delta.Difference ?? 0);
            var totalText = affectedTotal > 0 ? $" Total afectado: {FormatMoney(affectedTotal)}." : string.Empty;
            var productText = products.Count > 0
                ? $" Productos: {string.Join(", ", products.Distinct())}."
                : string.Empty;
            summary = $"Pedido #{log.EntityId} cancelado.{totalText}{productText}";
        }
        else if (string.Equals(log.EntityType, "expense_header", StringComparison.OrdinalIgnoreCase)
            && string.Equals(log.OperationType, "deleted", StringComparison.OrdinalIgnoreCase))
        {
            summary = FormatDeletedExpense(log, delta);
        }

        return new CashClosureAuditLogicalEvent
        {
            Id = log.Id,
            ChangedAt = log.ChangedAt,
            UserName = userName,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            OperationType = log.OperationType,
            SummaryText = summary,
            DetailText = WithActorAndColombiaTime(log.ChangedAt, userName, summary),
            TotalBefore = delta.TotalBefore,
            TotalAfter = delta.TotalAfter,
            Difference = delta.Difference ?? 0,
            HasProductChanges = delta.ProductChanges.Count > 0,
        };
    }

    private static string FormatDeletedExpense(EntityAuditLog log, AuditMoneyDelta delta)
    {
        if (string.IsNullOrWhiteSpace(log.BeforeJson))
            return log.SummaryText;

        try
        {
            using var doc = JsonDocument.Parse(log.BeforeJson);
            var snapshot = doc.RootElement;
            var total = TryGetDecimal(snapshot, "total") ?? delta.TotalBefore ?? 0;
            var vat = TryGetDecimal(snapshot, "vat_amount") ?? 0;
            var supplier = TryGetString(snapshot, "supplier_name");
            var deliveryman = TryGetString(snapshot, "deliveryman_name");
            var notes = TryGetString(snapshot, "notes");

            var parts = new List<string>
            {
                $"Gasto #{log.EntityId} eliminado.",
                $"Total: {FormatMoney(total)}.",
            };
            if (!string.IsNullOrWhiteSpace(supplier))
                parts.Add($"Proveedor: {supplier}.");
            if (vat > 0)
                parts.Add($"IVA incluido: {FormatMoney(vat)}.");
            if (!string.IsNullOrWhiteSpace(deliveryman))
                parts.Add($"Domiciliario: {deliveryman}.");

            var lines = FormatDeletedExpenseLines(snapshot);
            if (lines.Count > 0)
                parts.Add($"Detalle: {string.Join("; ", lines)}.");

            var payments = FormatDeletedExpensePayments(snapshot);
            if (payments.Count > 0)
                parts.Add($"Pagos: {string.Join("; ", payments)}.");
            var linkedAdvances = FormatDeletedExpenseLinkedAdvances(snapshot);
            if (linkedAdvances.Count > 0)
                parts.Add($"Abonos de domiciliario vinculados eliminados: {string.Join("; ", linkedAdvances)}.");
            if (!string.IsNullOrWhiteSpace(notes))
                parts.Add($"Notas: {notes}.");

            return string.Join(" ", parts);
        }
        catch (JsonException)
        {
            return log.SummaryText;
        }
    }

    private static IReadOnlyList<string> FormatDeletedExpenseLines(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return [];

        return lines.EnumerateArray()
            .Select(line =>
            {
                var name = TryGetString(line, "expense_name")
                    ?? (TryGetInt(line, "expense_id") is int id ? $"Gasto de catálogo #{id}" : "Línea");
                var category = TryGetString(line, "category_name");
                var quantity = TryGetDecimal(line, "quantity") ?? 0;
                var amount = TryGetDecimal(line, "amount") ?? 0;
                var total = TryGetDecimal(line, "total") ?? quantity * amount;
                var notes = TryGetString(line, "notes");
                var categoryText = string.IsNullOrWhiteSpace(category) ? string.Empty : $" [{category}]";
                var notesText = string.IsNullOrWhiteSpace(notes) ? string.Empty : $" ({notes})";
                return $"{FormatQuantity(quantity)} × {name}{categoryText} a {FormatMoney(amount)} = {FormatMoney(total)}{notesText}";
            })
            .ToList();
    }

    private static IReadOnlyList<string> FormatDeletedExpensePayments(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("payments", out var payments) || payments.ValueKind != JsonValueKind.Array)
            return [];

        return payments.EnumerateArray()
            .Select(payment =>
            {
                var name = TryGetString(payment, "bank_name")
                    ?? (TryGetInt(payment, "bank_id") is int id ? $"Banco #{id}" : "Banco");
                var amount = TryGetDecimal(payment, "amount") ?? 0;
                return $"{name}: {FormatMoney(amount)}";
            })
            .ToList();
    }

    private static IReadOnlyList<string> FormatDeletedExpenseLinkedAdvances(JsonElement snapshot)
    {
        if (!snapshot.TryGetProperty("linked_deliveryman_advances", out var advances)
            || advances.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return advances.EnumerateArray()
            .Select(advance =>
            {
                var id = TryGetInt(advance, "id");
                var amount = TryGetDecimal(advance, "amount") ?? 0;
                return $"{(id.HasValue ? $"#{id.Value}" : "sin identificador")}: {FormatMoney(amount)}";
            })
            .ToList();
    }

    private static string FormatProductChanges(
        IReadOnlyList<AuditProductChange> changes,
        IReadOnlyDictionary<int, string> productNames)
    {
        var removed = new List<string>();
        var added = new List<string>();
        var modified = new List<string>();

        foreach (var change in changes)
        {
            var beforeId = change.ProductIdBefore;
            var afterId = change.ProductIdAfter;
            var productChanged = beforeId.HasValue && afterId.HasValue && beforeId != afterId;

            if (beforeId.HasValue && (!afterId.HasValue || productChanged))
                removed.Add(QuantityAndProduct(change.QuantityBefore, ProductName(beforeId.Value, productNames)));
            if (afterId.HasValue && (!beforeId.HasValue || productChanged))
                added.Add(QuantityAndProduct(change.QuantityAfter, ProductName(afterId.Value, productNames)));

            if (!beforeId.HasValue || !afterId.HasValue || productChanged)
                continue;

            var name = ProductName(afterId.Value, productNames);
            if (change.QuantityBefore.HasValue
                && change.QuantityAfter.HasValue
                && change.QuantityBefore != change.QuantityAfter)
            {
                modified.Add($"cantidad de {name}: {FormatQuantity(change.QuantityBefore.Value)} → {FormatQuantity(change.QuantityAfter.Value)}");
            }
            if (change.UnitPriceBefore.HasValue
                && change.UnitPriceAfter.HasValue
                && change.UnitPriceBefore != change.UnitPriceAfter)
            {
                modified.Add($"precio de {name}: {FormatMoney(change.UnitPriceBefore.Value)} → {FormatMoney(change.UnitPriceAfter.Value)}");
            }
            if (change.DiscountBefore.HasValue
                && change.DiscountAfter.HasValue
                && change.DiscountBefore != change.DiscountAfter)
            {
                modified.Add($"descuento de {name}: {FormatMoney(change.DiscountBefore.Value)} → {FormatMoney(change.DiscountAfter.Value)}");
            }
        }

        var parts = new List<string>();
        if (removed.Count == 1 && added.Count == 1)
            parts.Add($"Cambio de {removed[0]} por {added[0]}.");
        else
        {
            if (removed.Count > 0)
                parts.Add($"Productos retirados: {string.Join(", ", removed)}.");
            if (added.Count > 0)
                parts.Add($"Productos agregados: {string.Join(", ", added)}.");
        }
        if (modified.Count > 0)
            parts.Add($"Cambios: {string.Join("; ", modified)}.");

        return parts.Count > 0 ? string.Join(" ", parts) : "Cambio monetario en productos.";
    }

    private static IReadOnlyList<AuditProductChange> TryGetProductChanges(JsonElement root)
    {
        if (!root.TryGetProperty("lines_affected", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return [];

        return lines.EnumerateArray()
            .Select(line =>
            {
                var legacyProductId = TryGetInt(line, "product_id");
                var quantityBefore = TryGetDecimal(line, "quantity_before");
                var quantityAfter = TryGetDecimal(line, "quantity_after");
                var productIdBefore = TryGetInt(line, "product_id_before");
                var productIdAfter = TryGetInt(line, "product_id_after");
                if (!productIdBefore.HasValue && !productIdAfter.HasValue)
                {
                    productIdBefore = quantityBefore.HasValue ? legacyProductId : null;
                    productIdAfter = quantityAfter.HasValue ? legacyProductId : null;
                }
                return new AuditProductChange
                {
                    ProductIdBefore = productIdBefore,
                    ProductIdAfter = productIdAfter,
                    QuantityBefore = quantityBefore,
                    QuantityAfter = quantityAfter,
                    UnitPriceBefore = TryGetDecimal(line, "unit_price_before"),
                    UnitPriceAfter = TryGetDecimal(line, "unit_price_after"),
                    DiscountBefore = TryGetDecimal(line, "discount_before"),
                    DiscountAfter = TryGetDecimal(line, "discount_after"),
                    SubtotalBefore = TryGetDecimal(line, "subtotal_before"),
                    SubtotalAfter = TryGetDecimal(line, "subtotal_after"),
                };
            })
            .ToList();
    }

    private static string GetOperationKey(EntityAuditLog log)
    {
        if (!string.IsNullOrWhiteSpace(log.MetadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(log.MetadataJson);
                if (doc.RootElement.TryGetProperty("operation_id", out var operationId))
                    return $"operation:{operationId}";
            }
            catch
            {
                // Legacy or malformed metadata falls back to PostgreSQL transaction time.
            }
        }

        return $"legacy:{log.ChangedAt.Ticks}:{log.ChangedByUserId?.ToString(CultureInfo.InvariantCulture) ?? "system"}";
    }

    private static bool IsDetailedNetReduction(CashClosureAuditLogicalEvent auditEvent) =>
        IsOrderModification(auditEvent)
        && auditEvent.Difference < 0
        && auditEvent.HasProductChanges;

    private static bool IsOrderModification(EntityAuditLog log) =>
        string.Equals(log.EntityType, "order", StringComparison.OrdinalIgnoreCase)
        && string.Equals(log.OperationType, "modified", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrderModification(CashClosureAuditLogicalEvent auditEvent) =>
        string.Equals(auditEvent.EntityType, "order", StringComparison.OrdinalIgnoreCase)
        && string.Equals(auditEvent.OperationType, "modified", StringComparison.OrdinalIgnoreCase);

    private static string WithActorAndColombiaTime(DateTime utc, string actor, string summary)
    {
        var changedAtColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(utc);
        return $"{changedAtColombia:HH:mm} - {actor} - {summary}";
    }

    private static string ActorName(EntityAuditLog log) =>
        string.IsNullOrWhiteSpace(log.ChangedByNameSnapshot) ? "Sistema" : log.ChangedByNameSnapshot;

    private static string ProductName(int id, IReadOnlyDictionary<int, string> productNames) =>
        productNames.TryGetValue(id, out var name) ? name : $"Producto #{id}";

    private static string QuantityAndProduct(decimal? quantity, string productName) =>
        quantity.HasValue ? $"{FormatQuantity(quantity.Value)} × {productName}" : productName;

    private static string FormatQuantity(decimal value) => value.ToString("0.##", ColombianCulture);
    private static string FormatMoney(decimal value) => $"${value.ToString("N0", ColombianCulture)}";

    private static int? TryGetInt(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(element.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static decimal? TryGetDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(element.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }
}

internal sealed class CashClosureAuditLogicalEvent
{
    public int Id { get; init; }
    public DateTime ChangedAt { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public int EntityId { get; init; }
    public string OperationType { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public decimal? TotalBefore { get; init; }
    public decimal? TotalAfter { get; init; }
    public decimal Difference { get; init; }
    public bool HasProductChanges { get; init; }
}

internal sealed class AuditMoneyDelta
{
    public decimal? TotalBefore { get; init; }
    public decimal? TotalAfter { get; init; }
    public decimal? Difference { get; init; }
    public IReadOnlyList<AuditProductChange> ProductChanges { get; init; } = [];
}

internal sealed class AuditProductChange
{
    public int? ProductIdBefore { get; init; }
    public int? ProductIdAfter { get; init; }
    public decimal? QuantityBefore { get; init; }
    public decimal? QuantityAfter { get; init; }
    public decimal? UnitPriceBefore { get; init; }
    public decimal? UnitPriceAfter { get; init; }
    public decimal? DiscountBefore { get; init; }
    public decimal? DiscountAfter { get; init; }
    public decimal? SubtotalBefore { get; init; }
    public decimal? SubtotalAfter { get; init; }
}
