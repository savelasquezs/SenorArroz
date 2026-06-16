using System.Text.Json;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

internal static class CashClosureAuditMapper
{
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
        "orders_modified" => "Pedidos modificados monetariamente",
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
                Difference = TryGetDecimal(root, "difference")
            };
        }
        catch
        {
            return new AuditMoneyDelta();
        }
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
}
