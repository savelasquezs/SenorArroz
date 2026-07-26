using SenorArroz.Application.Features.CashRegister.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Tests;

public class CashClosureAuditEmailTests
{
    private static readonly DateTime ChangedAt =
        new(2026, 7, 20, 18, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void MoreExpensiveReplacement_IsConsolidatedAndExcluded()
    {
        var events = Consolidate(
            Header(56000, 58000, operationId: "101"),
            Removed(productId: 10, subtotal: 56000, operationId: "101"),
            Added(productId: 11, subtotal: 58000, operationId: "101"));

        var auditEvent = Assert.Single(events);
        Assert.Equal(2000, auditEvent.Difference);
        Assert.False(CashClosureAuditMapper.ShouldIncludeInDailyEmail(auditEvent));
        Assert.False(CashClosureAuditMapper.ShouldIncludeInClosureAudit(auditEvent));
    }

    [Fact]
    public void EqualValueReplacement_IsConsolidatedAndExcluded()
    {
        var events = Consolidate(
            Removed(productId: 10, subtotal: 56000, operationId: "102"),
            Added(productId: 11, subtotal: 56000, operationId: "102"));

        var auditEvent = Assert.Single(events);
        Assert.Equal(0, auditEvent.Difference);
        Assert.False(CashClosureAuditMapper.ShouldIncludeInDailyEmail(auditEvent));
    }

    [Fact]
    public void CheaperReplacement_ReportsProductsActorColombiaTimeAndNetReduction()
    {
        var events = Consolidate(
            Header(56000, 50000, operationId: "103"),
            Removed(productId: 10, subtotal: 56000, operationId: "103"),
            Added(productId: 11, subtotal: 50000, operationId: "103"));

        var auditEvent = Assert.Single(events);
        Assert.Equal(-6000, auditEvent.Difference);
        Assert.Equal(56000, auditEvent.TotalBefore);
        Assert.Equal(50000, auditEvent.TotalAfter);
        Assert.True(CashClosureAuditMapper.ShouldIncludeInDailyEmail(auditEvent));
        Assert.Contains("Cambio de 1 × Ropa vieja Trío por 1 × Ropa vieja Familiar", auditEvent.SummaryText);
        Assert.Contains("Merma neta: $6.000", auditEvent.SummaryText);
        Assert.StartsWith("13:15 - Santiago -", auditEvent.DetailText);
    }

    [Fact]
    public void QuantityReduction_ReportsBeforeAndAfterWithoutDoubleCountingHeader()
    {
        var line = Audit(
            id: 2,
            moneyDelta:
                """{"total_before":82000,"total_after":74000,"difference":-8000,"lines_affected":[{"product_id":20,"product_id_before":20,"product_id_after":20,"quantity_before":2,"quantity_after":1,"unit_price_before":8000,"unit_price_after":8000,"discount_before":0,"discount_after":0,"subtotal_before":16000,"subtotal_after":8000}]}""",
            operationId: "104");
        var events = Consolidate(Header(82000, 74000, operationId: "104"), line);

        var auditEvent = Assert.Single(events);
        Assert.Equal(-8000, auditEvent.Difference);
        Assert.Contains("cantidad de CocaCola 1.5L: 2 → 1", auditEvent.SummaryText);
        Assert.Contains("Merma neta: $8.000", auditEvent.SummaryText);
    }

    [Fact]
    public void LegacyLogs_WithSameTransactionTimestamp_AreConsolidated()
    {
        var removed = Removed(productId: 10, subtotal: 56000, operationId: null);
        var added = Added(productId: 11, subtotal: 50000, operationId: null);

        var auditEvent = Assert.Single(Consolidate(removed, added));

        Assert.Equal(-6000, auditEvent.Difference);
        Assert.Contains("Ropa vieja Trío", auditEvent.SummaryText);
        Assert.Contains("Ropa vieja Familiar", auditEvent.SummaryText);
    }

    [Fact]
    public void Cancellation_RemainsASeparateAuditEvent()
    {
        var cancellation = Audit(
            id: 9,
            operationType: "cancelled",
            moneyDelta: """{"difference":0,"total_before":24000,"total_after":24000,"lines_affected":[]}""");

        var auditEvent = Assert.Single(Consolidate(cancellation));

        Assert.True(CashClosureAuditMapper.ShouldIncludeInDailyEmail(auditEvent));
        Assert.Equal("orders_cancelled", CashClosureAuditMapper.GroupKey(auditEvent));
        Assert.Contains("Total afectado: $24.000", auditEvent.SummaryText);
    }

    [Fact]
    public void DeletedExpense_IsIncludedWithItsCompleteSnapshot()
    {
        var deletedExpense = Audit(
            id: 10,
            operationType: "deleted",
            moneyDelta: """{"total_before":25840,"total_after":0,"difference":-25840,"lines_affected":[]}""");
        deletedExpense.EntityType = "expense_header";
        deletedExpense.EntityId = 321;
        deletedExpense.BeforeJson =
            """
            {
              "id": 321,
              "supplier_name": "Distribuciones Centro",
              "deliveryman_name": "Carlos Pérez",
              "total": 25840,
              "vat_amount": 3040,
              "notes": "Compra urgente",
              "lines": [
                {
                  "expense_id": 8,
                  "expense_name": "Arroz",
                  "category_name": "Insumos",
                  "quantity": 2,
                  "amount": 10000,
                  "total": 20000,
                  "notes": "Bulto pequeño"
                },
                {
                  "expense_id": 9,
                  "expense_name": "Transporte",
                  "category_name": "Logística",
                  "quantity": 1,
                  "amount": 2800,
                  "total": 2800
                }
              ],
              "payments": [
                { "bank_id": 4, "bank_name": "Bancolombia", "amount": 25840 }
              ]
            }
            """;

        var auditEvent = Assert.Single(Consolidate(deletedExpense));

        Assert.True(CashClosureAuditMapper.ShouldIncludeInDailyEmail(auditEvent));
        Assert.Equal("expenses_deleted", CashClosureAuditMapper.GroupKey(auditEvent));
        Assert.Contains("Gasto #321 eliminado", auditEvent.SummaryText);
        Assert.Contains("Proveedor: Distribuciones Centro", auditEvent.SummaryText);
        Assert.Contains("2 × Arroz [Insumos] a $10.000 = $20.000 (Bulto pequeño)", auditEvent.SummaryText);
        Assert.Contains("1 × Transporte [Logística] a $2.800 = $2.800", auditEvent.SummaryText);
        Assert.Contains("Pagos: Bancolombia: $25.840", auditEvent.SummaryText);
        Assert.Contains("Notas: Compra urgente", auditEvent.SummaryText);
    }

    [Fact]
    public void DailyTrackingAudit_IncludesOnlyDelicateLocationAlerts()
    {
        Assert.Equal(
            [
                DeliveryTrackingAlertType.GpsDisabled,
                DeliveryTrackingAlertType.LocationPermissionRevoked,
                DeliveryTrackingAlertType.UnexpectedStay,
            ],
            CashClosureAuditMapper.IncludedTrackingAlertTypes);
        Assert.DoesNotContain(
            DeliveryTrackingAlertType.NoCommunication,
            CashClosureAuditMapper.IncludedTrackingAlertTypes);
        Assert.DoesNotContain(
            DeliveryTrackingAlertType.OfflineLocationsQueued,
            CashClosureAuditMapper.IncludedTrackingAlertTypes);
        Assert.DoesNotContain(
            DeliveryTrackingAlertType.SessionPastAutoClose,
            CashClosureAuditMapper.IncludedTrackingAlertTypes);
    }

    private static IReadOnlyList<CashClosureAuditLogicalEvent> Consolidate(params EntityAuditLog[] logs) =>
        CashClosureAuditMapper.Consolidate(
            logs,
            new Dictionary<int, string>
            {
                [10] = "Ropa vieja Trío",
                [11] = "Ropa vieja Familiar",
                [20] = "CocaCola 1.5L",
            },
            new Dictionary<int, IReadOnlyList<string>>());

    private static EntityAuditLog Header(decimal before, decimal after, string? operationId) =>
        Audit(
            id: 3,
            moneyDelta:
                $$"""{"total_before":{{before}},"total_after":{{after}},"difference":{{after - before}},"lines_affected":[]}""",
            operationId: operationId);

    private static EntityAuditLog Removed(int productId, decimal subtotal, string? operationId) =>
        Audit(
            id: 1,
            moneyDelta:
                $$"""{"total_before":{{subtotal * 2}},"total_after":{{subtotal}},"difference":-{{subtotal}},"lines_affected":[{"product_id":{{productId}},"product_id_before":{{productId}},"quantity_before":1,"unit_price_before":{{subtotal}},"discount_before":0,"subtotal_before":{{subtotal}}}]}""",
            operationId: operationId);

    private static EntityAuditLog Added(int productId, decimal subtotal, string? operationId) =>
        Audit(
            id: 2,
            moneyDelta:
                $$"""{"total_before":0,"total_after":{{subtotal}},"difference":{{subtotal}},"lines_affected":[{"product_id":{{productId}},"product_id_after":{{productId}},"quantity_after":1,"unit_price_after":{{subtotal}},"discount_after":0,"subtotal_after":{{subtotal}}}]}""",
            operationId: operationId);

    private static EntityAuditLog Audit(
        int id,
        string moneyDelta,
        string operationType = "modified",
        string? operationId = null) => new()
    {
        Id = id,
        EntityType = "order",
        EntityId = 4644,
        OperationType = operationType,
        MoneyDeltaJson = moneyDelta,
        MetadataJson = operationId == null ? null : $$"""{"operation_id":"{{operationId}}"}""",
        ChangedAt = ChangedAt,
        ChangedByUserId = 7,
        ChangedByNameSnapshot = "Santiago",
        SummaryText = "Evento de prueba",
    };
}
