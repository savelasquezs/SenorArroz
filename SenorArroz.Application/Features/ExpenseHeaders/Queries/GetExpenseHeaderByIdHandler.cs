using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseHeaders.Queries;

public class GetExpenseHeaderByIdHandler : IRequestHandler<GetExpenseHeaderByIdQuery, ExpenseHeaderDto?>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetExpenseHeaderByIdHandler(IExpenseHeaderRepository expenseHeaderRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<ExpenseHeaderDto?> Handle(GetExpenseHeaderByIdQuery request, CancellationToken cancellationToken)
    {
        var expenseHeader = await _expenseHeaderRepository.GetByIdWithDetailsAsync(request.Id);

        if (expenseHeader == null)
        {
            return null;
        }

        // Validar acceso
        if (_currentUser.Role != "superadmin")
        {
            // Debe ser de la misma sucursal
            if (expenseHeader.BranchId != _currentUser.BranchId)
            {
                return null; // No tiene acceso
            }

            // Si es cashier, solo puede ver sus propios gastos
            if (_currentUser.Role == "cashier" && expenseHeader.CreatedById != _currentUser.Id)
            {
                return null; // No tiene acceso
            }
        }

        var dto = _mapper.Map<ExpenseHeaderDto>(expenseHeader);

        // Calcular campos calculados
        dto.CategoryNames = dto.ExpenseDetails
            .Select(ed => ed.ExpenseCategoryName)
            .Distinct()
            .ToList();

        dto.BankNames = dto.ExpenseBankPayments
            .Select(ebp => ebp.BankName)
            .Distinct()
            .ToList();

        dto.ExpenseNames = dto.ExpenseDetails
            .Select(ed => ed.ExpenseName)
            .Distinct()
            .ToList();

        return dto;
    }
}


