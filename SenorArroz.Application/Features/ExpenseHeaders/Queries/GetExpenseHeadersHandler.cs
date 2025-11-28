using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.ExpenseHeaders.Queries;

public class GetExpenseHeadersHandler : IRequestHandler<GetExpenseHeadersQuery, PagedResult<ExpenseHeaderDto>>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetExpenseHeadersHandler(IExpenseHeaderRepository expenseHeaderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ExpenseHeaderDto>> Handle(GetExpenseHeadersQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        int? createdByIdFilter = null;

        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;

            // Si es cashier, solo ve sus propios gastos
            if (_currentUser.Role == "cashier")
            {
                createdByIdFilter = _currentUser.Id;
            }
        }
        else if (request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }

        // Manejar fechas: día actual por defecto si no se envían
        DateTime? fromDateUtc = null;
        DateTime? toDateUtc = null;

        if (request.FromDate.HasValue)
        {
            fromDateUtc = ColombiaTimeHelper.ConvertColombiaToUtc(request.FromDate.Value);
        }
        else
        {
            // Si no se envía fecha, usar inicio del día actual en Colombia
            fromDateUtc = ColombiaTimeHelper.GetTodayStartInUtc();
        }

        if (request.ToDate.HasValue)
        {
            toDateUtc = ColombiaTimeHelper.ConvertColombiaToUtc(request.ToDate.Value);
        }
        else if (request.FromDate.HasValue)
        {
            // Frontend envió FromDate pero no ToDate: usar el fin del mismo día de FromDate
            var endOfFromDate = request.FromDate.Value.Date.AddDays(1).AddTicks(-1);
            toDateUtc = ColombiaTimeHelper.ConvertColombiaToUtc(endOfFromDate);
        }
        else
        {
            // Si no se envía fecha, usar fin del día actual en Colombia
            toDateUtc = ColombiaTimeHelper.GetTodayEndInUtc();
        }

        var result = await _expenseHeaderRepository.GetPagedAsync(
            branchFilter,
            null, // supplierId - no se filtra en el query, se hace localmente
            createdByIdFilter,
            fromDateUtc,
            toDateUtc,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var expenseHeaderDtos = _mapper.Map<List<ExpenseHeaderDto>>(result.Items);

        // Calcular campos calculados para filtros locales
        foreach (var dto in expenseHeaderDtos)
        {
            // Categorías únicas
            dto.CategoryNames = dto.ExpenseDetails
                .Select(ed => ed.ExpenseCategoryName)
                .Distinct()
                .ToList();

            // Bancos de los pagos
            dto.BankNames = dto.ExpenseBankPayments
                .Select(ebp => ebp.BankName)
                .Distinct()
                .ToList();

            // Nombres de gastos
            dto.ExpenseNames = dto.ExpenseDetails
                .Select(ed => ed.ExpenseName)
                .Distinct()
                .ToList();
        }

        return new PagedResult<ExpenseHeaderDto>
        {
            Items = expenseHeaderDtos,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }
}


