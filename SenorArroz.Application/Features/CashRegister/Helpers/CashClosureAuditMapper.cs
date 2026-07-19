using System.Globalization;
using System.Text.Json;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

internal static class CashClosureAuditMapper
{
    private static readonly CultureInfo ColombianCulture = CultureInfo.GetCultureInfo("es-CO");

    public static bool ShouldIncludeInDailyEmail(EntityAuditLog log)
    {
        if (!string.Equals(log.EntityType, "order", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(log.OperationType, "cancelled", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.Equals(log.OperationType, "modified", StringComparison.OrdinalIgnoreCase))
            return false;

        var delta = ParseDelta(log.MoneyDeltaJson);
        return delta.Difference < 0 && delta.ProductIds.Count > 0;
    }

    public static string FormatDailyEmailDetail(
        EntityAuditLog log,
        IReadOnlyDictionary<int, string> productNames,
        IReadOnlyDictionary<int, IReadOnlyList<string>> orderProductNames)
    {
        var changedAtColombia = ColombiaTimeHelper.GetNowInColombiaFromUtc(log.ChangedAt);
        var actor = string.IsNullOrWhiteSpace(log.ChangedByNameSnapshot) ? "Sistema" : log.ChangedByNameSnapshot;
        var delta = ParseDelta(log.MoneyDeltaJson);
        var products = delta.ProductIds
            .Distinct()
            .Select(id => productNames.TryGetValue(id, out var name) ? name : $"Producto #{id}")
            .ToList();
        if (products.Count == 0 && orderProductNames.TryGetValue(log.EntityId, out var namesFromOrder))
            products.AddRange(namesFromOrder);
        var productText = products.Count == 0
            ? string.Empty
            : $" Producto{(products.Count == 1 ? string.Empty : "s")}: {string.Join(", ", products.Distinct())}.";

        if (string.Equals(log.OperationType, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            var affectedTotal = delta.TotalBefore ?? Math.Abs(delta.Difference ?? 0);
            var totalText = affectedTotal > 0 ? $" Total afectado: {FormatMoney(affectedTotal)}." : string.Empty;
            return $"{changedAtColombia:HH:mm} - {actor} - Pedido #{log.EntityId} cancelado.{totalText}{productText}";
        }

        var reduction = Math.Abs(delta.Difference ?? 0);
        var totalsText = delta.TotalBefore.HasValue && delta.TotalAfter.HasValue
            ? $" El valor bajó de {FormatMoney(delta.TotalBefore.Value)} a {FormatMoney(delta.TotalAfter.Value)}."
            : string.Empty;
        return $"{changedAtColombia:HH:mm} - {actor} - Pedido #{log.EntityId}: reducción de {FormatMoney(reduction)}.{productText}{totalsText}";
    }

    private static string FormatMoney(decimal value) => $"${value.ToString("N0", ColombianCulture)}";

    public static CashClosureAuditEventDto ToDto(EntityAuditLog log)
    {
        var delta = ParseDelta(log.MoneyDeltaJson);

        return new CashClosureAuditEventDto
        {
            Id = log.Id,
            ChangedAt = log.ChangedAt,
            UserName = string.IsNullOrWhiteSpace(log.ChangedByNameSnapshot) ? "Sistema" : log.ChangedByNameSnapshot,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            OperationType = log.OperationType,
            SummaryText = log.SummaryText,
            TotalBefore = delta.TotalBefore,
            TotalAfter = delta.TotalAfter,
            Difference = delta.Difference ?? 0
        };
    }

    public static string GroupKey(EntityAuditLog log) =>
        (log.EntityType, log.OperationType) switch
        {
            ("order", "cancelled") => "orders_cancelled",
            ("order", "deleted") => "orders_deleted",
            ("order", _) => "orders_modified",
            ("expense_header", "deleted") => "expenses_deleted",
            ("expense_header", _) => "expenses_modified",
            _ => "other"
        };

    public static string GroupTitle(string key) => key switch
    {
        "orders_cancelled" => "Pedidos cancelados",
        "orders_deleted" => "Pedidos eliminados",
        "orders_modified" => "Reducciones monetarias en pedidos",
        "expenses_deleted" => "Gastos eliminados",
        "expenses_modified" => "Gastos modificados monetariamente",
        _ => "Otros"
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
                ProductIds = TryGetProductIds(root)
            };
        }
        catch
        {
            return new AuditMoneyDelta();
        }
    }

    private static IReadOnlyList<int> TryGetProductIds(JsonElement root)
    {
        if (!root.TryGetProperty("lines_affected", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return [];

        return lines.EnumerateArray()
            .Where(line => line.TryGetProperty("product_id", out var productId) && productId.TryGetInt32(out _))
            .Select(line => line.GetProperty("product_id").GetInt32())
            .ToList();
    }

    private static decimal? TryGetDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return null;

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }
}

internal sealed class AuditMoneyDelta
{
    public decimal? TotalBefore { get; init; }
    public decimal? TotalAfter { get; init; }
    public decimal? Difference { get; init; }
    public IReadOnlyList<int> ProductIds { get; init; } = [];
}
