using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;

namespace SenorArroz.Application.Features.ExpenseHeaders.Helpers;

public static class ExpenseHeaderLinkedAdvancePopulator
{
    public static async Task PopulateAsync(
        IApplicationDbContext context,
        IEnumerable<ExpenseHeaderDto> dtos,
        CancellationToken cancellationToken = default)
    {
        var list = dtos as IList<ExpenseHeaderDto> ?? dtos.ToList();
        if (list.Count == 0)
            return;

        var ids = list.Select(d => d.Id).ToList();
        var rows = await context.DeliverymanAdvances.AsNoTracking()
            .Where(a => a.ExpenseHeaderId != null && ids.Contains(a.ExpenseHeaderId.Value))
            .Select(a => new { HeaderId = a.ExpenseHeaderId!.Value, a.Id, a.Amount })
            .ToListAsync(cancellationToken);

        var map = rows
            .GroupBy(r => r.HeaderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());

        foreach (var dto in list)
        {
            if (map.TryGetValue(dto.Id, out var row))
            {
                dto.LinkedDeliverymanAdvanceId = row.Id;
                dto.LinkedDeliverymanAdvanceAmount = row.Amount;
            }
        }
    }
}
