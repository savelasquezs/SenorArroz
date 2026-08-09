using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Application.Features.ExpenseHeaders.Helpers;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ExpenseHeaders.Queries;

public class GetExpenseHeadersHandler : IRequestHandler<GetExpenseHeadersQuery, PagedResult<ExpenseHeaderDto>>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public GetExpenseHeadersHandler(
        IExpenseHeaderRepository expenseHeaderRepository,
        IApplicationDbContext context,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _context = context;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<PagedResult<ExpenseHeaderDto>> Handle(GetExpenseHeadersQuery request, CancellationToken cancellationToken)
    {
        int? branchFilter = _branchContext.ResolveOptional(request.BranchId);
        int? createdByIdFilter = null;

        if (!Roles.IsSuperadmin(_currentUser.Role))
        {
            branchFilter = _currentUser.BranchId;

        }
        else if (request.BranchId > 0)
        {
            branchFilter = request.BranchId;
        }

        DateTime fromDateUtc;
        DateTime toDateUtc;

        if (!request.FromDate.HasValue && !request.ToDate.HasValue)
        {
            fromDateUtc = ColombiaTimeHelper.GetTodayStartInUtc();
            toDateUtc = ColombiaTimeHelper.GetTodayEndInUtc();
        }
        else
        {
            var fromCal = request.FromDate ?? request.ToDate!.Value;
            var toCal = request.ToDate ?? request.FromDate!.Value;
            (fromDateUtc, toDateUtc) = ColombiaTimeHelper.GetColombiaCalendarDateRangeUtc(fromCal, toCal);
        }

        var result = await _expenseHeaderRepository.GetPagedAsync(
            branchFilter,
            request.SupplierIds,
            createdByIdFilter,
            fromDateUtc,
            toDateUtc,
            request.BankNames,
            request.CategoryNames,
            request.ExpenseName,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            cancellationToken);

        var expenseHeaderDtos = _mapper.Map<List<ExpenseHeaderDto>>(result.Items);
        var normalizedCategoryNames = NormalizeStringFilters(request.CategoryNames);
        var normalizedExpenseName = string.IsNullOrWhiteSpace(request.ExpenseName)
            ? null
            : request.ExpenseName.Trim().ToLower();

        foreach (var dto in expenseHeaderDtos)
        {
            if (normalizedCategoryNames.Count > 0 || normalizedExpenseName is not null)
            {
                dto.ExpenseDetails = dto.ExpenseDetails
                    .Where(detail => MatchesDetail(detail, normalizedCategoryNames, normalizedExpenseName))
                    .ToList();

                dto.Total = dto.ExpenseDetails.Sum(LineNumericTotal);
            }

            dto.CategoryNames = dto.ExpenseDetails
                .Select(ed => ed.ExpenseCategoryName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();

            dto.BankNames = dto.ExpenseBankPayments
                .Select(ebp => ebp.BankName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();

            dto.ExpenseNames = dto.ExpenseDetails
                .Select(ed => ed.ExpenseName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToList();
        }

        await ExpenseHeaderLinkedAdvancePopulator.PopulateAsync(_context, expenseHeaderDtos, cancellationToken);

        return new PagedResult<ExpenseHeaderDto>
        {
            Items = expenseHeaderDtos,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }

    private static List<string> NormalizeStringFilters(IEnumerable<string>? values)
    {
        if (values is null) return new List<string>();

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLower())
            .Distinct()
            .ToList();
    }

    private static bool MatchesDetail(
        ExpenseDetailDto detail,
        IReadOnlyCollection<string> normalizedCategoryNames,
        string? normalizedExpenseName)
    {
        if (normalizedCategoryNames.Count > 0)
        {
            var categoryName = (detail.ExpenseCategoryName ?? string.Empty).Trim().ToLower();
            if (!normalizedCategoryNames.Contains(categoryName))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedExpenseName))
        {
            var expenseName = (detail.ExpenseName ?? string.Empty).Trim().ToLower();
            if (!expenseName.Contains(normalizedExpenseName))
            {
                return false;
            }
        }

        return true;
    }

    private static decimal LineNumericTotal(ExpenseDetailDto detail)
    {
        if (detail.Total.HasValue)
        {
            return detail.Total.Value;
        }

        return detail.Quantity * detail.Amount;
    }
}
