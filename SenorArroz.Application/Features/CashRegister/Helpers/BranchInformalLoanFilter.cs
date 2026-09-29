using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.CashRegister.Helpers;

public static class BranchInformalLoanFilter
{
    public static IQueryable<BranchInformalLoan> Apply(
        IQueryable<BranchInformalLoan> query,
        string? source,
        int? bankId)
    {
        var normalized = string.IsNullOrWhiteSpace(source) ? "all" : source.Trim().ToLowerInvariant();
        if (normalized is not ("all" or "cash" or "bank"))
            throw new BusinessException("El origen debe ser all, cash o bank");
        if (normalized == "cash" && bankId.HasValue)
            throw new BusinessException("No se puede filtrar efectivo por banco");

        if (bankId.HasValue)
            return query.Where(loan => loan.BankId == bankId.Value);

        return normalized switch
        {
            "cash" => query.Where(loan => loan.BankId == null),
            "bank" => query.Where(loan => loan.BankId != null),
            _ => query
        };
    }
}
