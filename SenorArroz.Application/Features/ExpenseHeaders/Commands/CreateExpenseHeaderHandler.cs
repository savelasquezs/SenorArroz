using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class CreateExpenseHeaderHandler : IRequestHandler<CreateExpenseHeaderCommand, ExpenseHeaderDto>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateExpenseHeaderHandler(
        IExpenseHeaderRepository expenseHeaderRepository,
        IBankRepository bankRepository,
        IApplicationDbContext context,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _bankRepository = bankRepository;
        _context = context;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<ExpenseHeaderDto> Handle(CreateExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        // Validar que el supplier existe
        var supplier = await _context.Suppliers.FindAsync(new object[] { request.ExpenseHeader.SupplierId }, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException($"Proveedor con ID {request.ExpenseHeader.SupplierId} no encontrado");
        }

        // Validar que los expenses existen
        var expenseIds = request.ExpenseHeader.ExpenseDetails.Select(ed => ed.ExpenseId).Distinct().ToList();
        var expenses = await _context.Expenses
            .Where(e => expenseIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        if (expenses.Count != expenseIds.Count)
        {
            var foundIds = expenses.Select(e => e.Id).ToList();
            var missingIds = expenseIds.Except(foundIds).ToList();
            throw new NotFoundException($"Gastos con IDs {string.Join(", ", missingIds)} no encontrados");
        }

        // Validar que los banks existen y pertenecen a la sucursal
        var branchId = _currentUser.BranchId;
        if (request.ExpenseHeader.ExpenseBankPayments != null && request.ExpenseHeader.ExpenseBankPayments.Any())
        {
            var bankIds = request.ExpenseHeader.ExpenseBankPayments.Select(ebp => ebp.BankId).Distinct().ToList();
            var banks = await _context.Banks
                .Where(b => bankIds.Contains(b.Id) && b.BranchId == branchId)
                .ToListAsync(cancellationToken);

            if (banks.Count != bankIds.Count)
            {
                var foundBankIds = banks.Select(b => b.Id).ToList();
                var missingBankIds = bankIds.Except(foundBankIds).ToList();
                throw new NotFoundException($"Bancos con IDs {string.Join(", ", missingBankIds)} no encontrados o no pertenecen a la sucursal");
            }

            // Validar que la suma de pagos bancarios no exceda el total (se calculará después)
            var totalBankPayments = request.ExpenseHeader.ExpenseBankPayments.Sum(ebp => (decimal)ebp.Amount);
            var totalExpenseDetails = request.ExpenseHeader.ExpenseDetails.Sum(ed => (decimal)(ed.Quantity * ed.Amount));
            
            if (totalBankPayments > totalExpenseDetails)
            {
                throw new BusinessException("La suma de pagos bancarios no puede exceder el total de los gastos");
            }
        }

        // Crear ExpenseHeader
        var expenseHeader = new ExpenseHeader
        {
            BranchId = branchId,
            SupplierId = request.ExpenseHeader.SupplierId,
            CreatedById = _currentUser.Id,
            ExpenseDetails = request.ExpenseHeader.ExpenseDetails.Select(ed => new ExpenseDetail
            {
                ExpenseId = ed.ExpenseId,
                Quantity = ed.Quantity,
                Amount = ed.Amount
            }).ToList(),
            ExpenseBankPayments = request.ExpenseHeader.ExpenseBankPayments?.Select(ebp => new ExpenseBankPayment
            {
                BankId = ebp.BankId,
                Amount = ebp.Amount
            }).ToList() ?? new List<ExpenseBankPayment>()
        };

        var created = await _expenseHeaderRepository.CreateAsync(expenseHeader);
        var createdWithDetails = await _expenseHeaderRepository.GetByIdWithDetailsAsync(created.Id);

        if (createdWithDetails == null)
        {
            throw new BusinessException("Error al crear el gasto");
        }

        var dto = _mapper.Map<ExpenseHeaderDto>(createdWithDetails);

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

