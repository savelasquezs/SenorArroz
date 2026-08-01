using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Enums;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

public static class CashRegisterUnsettledAppsHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Pagos vía app aún no liquidados, por app, pedidos entregados de la sucursal.
    /// </summary>
    public static async Task<(List<UnsettledAppLineDto> Lines, decimal Total)> LoadUnsettledForBranchAsync(
        IApplicationDbContext context,
        int branchId,
        CancellationToken cancellationToken)
    {
        var raw = await context.AppPayments
            .AsNoTracking()
            .Where(ap => !ap.IsSetted
                         && !ap.IsReversed
                         && ap.Order.BranchId == branchId
                         && ap.Order.Status == OrderStatus.Delivered)
            .Select(ap => new
            {
                ap.AppId,
                AppName = ap.App.Name,
                Amount = ap.ExpectedNetAmount ?? ap.Amount
            })
            .ToListAsync(cancellationToken);

        var lines = raw
            .GroupBy(x => (x.AppId, x.AppName))
            .Select(g => new UnsettledAppLineDto
            {
                AppId = g.Key.AppId,
                AppName = g.Key.AppName ?? "",
                Amount = g.Sum(x => x.Amount),
            })
            .OrderBy(x => x.AppName)
            .ToList();

        var total = lines.Sum(x => x.Amount);
        return (lines, total);
    }

    public static string SerializeSnapshot(IReadOnlyList<UnsettledAppLineDto> lines) =>
        JsonSerializer.Serialize(lines, JsonOptions);

    public static decimal SumSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return 0;
        try
        {
            var list = JsonSerializer.Deserialize<List<UnsettledAppLineDto>>(json, JsonOptions);
            return list?.Sum(x => x.Amount) ?? 0;
        }
        catch
        {
            return 0;
        }
    }
}
