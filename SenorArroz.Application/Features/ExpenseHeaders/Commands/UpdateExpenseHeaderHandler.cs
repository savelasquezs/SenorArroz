using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.ExpenseHeaders.Commands;

public class UpdateExpenseHeaderHandler : IRequestHandler<UpdateExpenseHeaderCommand, ExpenseHeaderDto>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public UpdateExpenseHeaderHandler(
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

    public async Task<ExpenseHeaderDto> Handle(UpdateExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        var expenseHeader = await _expenseHeaderRepository.GetByIdWithDetailsAsync(request.Id);

        if (expenseHeader == null)
        {
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");
        }

        // Validar acceso
        if (_currentUser.Role != "superadmin")
        {
            if (expenseHeader.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes acceso a este gasto");
            }

            if (_currentUser.Role == "cashier" && expenseHeader.CreatedById != _currentUser.Id)
            {
                throw new BusinessException("Solo puedes editar tus propios gastos");
            }
        }

        // Actualizar supplier si se proporciona
        if (request.ExpenseHeader.SupplierId.HasValue)
        {
            var supplier = await _context.Suppliers.FindAsync(new object[] { request.ExpenseHeader.SupplierId.Value }, cancellationToken);
            if (supplier == null)
            {
                throw new NotFoundException($"Proveedor con ID {request.ExpenseHeader.SupplierId.Value} no encontrado");
            }
            expenseHeader.SupplierId = request.ExpenseHeader.SupplierId.Value;
        }

        // Manejar detalles: actualizar existentes, crear nuevos, eliminar los que no están en la lista
        if (request.ExpenseHeader.ExpenseDetails != null)
        {
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

            // Obtener IDs de detalles existentes que se están actualizando
            var existingDetailIds = request.ExpenseHeader.ExpenseDetails
                .Where(ed => ed.Id.HasValue)
                .Select(ed => ed.Id!.Value)
                .ToList();

            // Eliminar detalles que no están en la lista
            var detailsToRemove = expenseHeader.ExpenseDetails
                .Where(ed => !existingDetailIds.Contains(ed.Id))
                .ToList();

            foreach (var detail in detailsToRemove)
            {
                _context.ExpenseDetails.Remove(detail);
            }

            // Actualizar o crear detalles
            foreach (var detailDto in request.ExpenseHeader.ExpenseDetails)
            {
                if (detailDto.Id.HasValue)
                {
                    // Actualizar existente
                    var existingDetail = expenseHeader.ExpenseDetails.FirstOrDefault(ed => ed.Id == detailDto.Id.Value);
                    if (existingDetail != null)
                    {
                        existingDetail.ExpenseId = detailDto.ExpenseId;
                        existingDetail.Quantity = detailDto.Quantity;
                        existingDetail.Amount = detailDto.Amount;
                    }
                }
                else
                {
                    // Crear nuevo
                    expenseHeader.ExpenseDetails.Add(new ExpenseDetail
                    {
                        ExpenseId = detailDto.ExpenseId,
                        Quantity = detailDto.Quantity,
                        Amount = detailDto.Amount
                    });
                }
            }
        }

        // Manejar pagos: solo agregar nuevos (no se pueden editar/eliminar pagos existentes)
        if (request.ExpenseHeader.ExpenseBankPayments != null && request.ExpenseHeader.ExpenseBankPayments.Any())
        {
            var branchId = _currentUser.BranchId;
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

            // Agregar nuevos pagos
            foreach (var paymentDto in request.ExpenseHeader.ExpenseBankPayments)
            {
                expenseHeader.ExpenseBankPayments.Add(new ExpenseBankPayment
                {
                    BankId = paymentDto.BankId,
                    Amount = paymentDto.Amount
                });
            }
        }

        var updated = await _expenseHeaderRepository.UpdateAsync(expenseHeader);
        var updatedWithDetails = await _expenseHeaderRepository.GetByIdWithDetailsAsync(updated.Id);

        if (updatedWithDetails == null)
        {
            throw new BusinessException("Error al actualizar el gasto");
        }

        var dto = _mapper.Map<ExpenseHeaderDto>(updatedWithDetails);

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

