using System.Text.Json;

namespace SenorArroz.Application.Features.AppPayments.Commands;

internal static class AppSettlementBankPaymentSourceIds
{
    public static string Serialize(IEnumerable<int> appPaymentIds)
    {
        var ids = appPaymentIds
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        return JsonSerializer.Serialize(ids);
    }

    public static List<int> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<int>();

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }
}
