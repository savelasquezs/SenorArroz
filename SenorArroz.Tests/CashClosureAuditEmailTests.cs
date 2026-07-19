using SenorArroz.Application.Features.CashRegister.Helpers;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Tests;

public class CashClosureAuditEmailTests
{
    [Fact]
    public void IncludesOnlyCancellationsAndDetailedNegativeChanges()
    {
        var cancellation = Audit("cancelled", """{"difference":-24000,"total_before":24000}""");
        var negativeProductChange = Audit("modified", """{"difference":-3000,"total_before":73000,"total_after":70000,"lines_affected":[{"product_id":20}]}""");
        var positiveProductChange = Audit("modified", """{"difference":3000,"lines_affected":[{"product_id":20}]}""");
        var genericRecalculation = Audit("modified", """{"difference":-20000,"total_before":89000,"total_after":69000,"lines_affected":[]}""");

        Assert.True(CashClosureAuditMapper.ShouldIncludeInDailyEmail(cancellation));
        Assert.True(CashClosureAuditMapper.ShouldIncludeInDailyEmail(negativeProductChange));
        Assert.False(CashClosureAuditMapper.ShouldIncludeInDailyEmail(positiveProductChange));
        Assert.False(CashClosureAuditMapper.ShouldIncludeInDailyEmail(genericRecalculation));
    }

    [Fact]
    public void FormatsColombiaTimeAndProductName()
    {
        var log = Audit("modified", """{"difference":-3000,"total_before":73000,"total_after":70000,"lines_affected":[{"product_id":20}]}""");
        log.EntityId = 4359;
        log.ChangedAt = new DateTime(2026, 7, 20, 3, 15, 0, DateTimeKind.Utc);
        log.ChangedByNameSnapshot = "Santiago";

        var detail = CashClosureAuditMapper.FormatDailyEmailDetail(
            log,
            new Dictionary<int, string> { [20] = "Arroz con pollo" },
            new Dictionary<int, IReadOnlyList<string>>());

        Assert.StartsWith("22:15 - Santiago", detail);
        Assert.Contains("Arroz con pollo", detail);
        Assert.Contains("reducción de $3.000", detail);
        Assert.DoesNotContain("Producto #20", detail);
    }

    [Fact]
    public void AddsProductsFromCancelledOrder()
    {
        var log = Audit("cancelled", """{"difference":0,"total_before":24000,"lines_affected":[]}""");
        log.EntityId = 4431;

        var detail = CashClosureAuditMapper.FormatDailyEmailDetail(
            log,
            new Dictionary<int, string>(),
            new Dictionary<int, IReadOnlyList<string>>
            {
                [4431] = ["Arroz paisa", "Limonada"]
            });

        Assert.Contains("Arroz paisa, Limonada", detail);
        Assert.Contains("Total afectado: $24.000", detail);
    }

    private static EntityAuditLog Audit(string operationType, string deltaJson) => new()
    {
        EntityType = "order",
        OperationType = operationType,
        MoneyDeltaJson = deltaJson,
        ChangedAt = DateTime.UtcNow
    };
}
