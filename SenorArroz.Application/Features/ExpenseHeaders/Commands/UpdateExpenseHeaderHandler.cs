using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.ExpenseHeaders.DTOs;
using SenorArroz.Application.Features.ExpenseHeaders.Helpers;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
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
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;

    public UpdateExpenseHeaderHandler(
        IExpenseHeaderRepository expenseHeaderRepository,
        IBankRepository bankRepository,
        IApplicationDbContext context,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IClock clock)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _bankRepository = bankRepository;
        _context = context;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _clock = clock;
    }

    public async Task<ExpenseHeaderDto> Handle(UpdateExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        var expenseHeader = await _expenseHeaderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);

        if (expenseHeader == null)
        {
            throw new NotFoundException($"Gasto con ID {request.Id} no encontrado");
        }
        _branchContext.EnsureAccess(expenseHeader.BranchId);

        // Validar acceso
        if (!Roles.IsSuperadmin(_currentUser.Role))
        {
            if (expenseHeader.BranchId != _currentUser.BranchId)
            {
                throw new BusinessException("No tienes acceso a este gasto");
            }

            if (Roles.IsCashier(_currentUser.Role) && expenseHeader.CreatedById != _currentUser.Id)
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
            _branchContext.EnsureAccess(supplier.BranchId);
            if (supplier.BranchId != expenseHeader.BranchId)
                throw new BranchScopeMismatchException();
            expenseHeader.SupplierId = request.ExpenseHeader.SupplierId.Value;
        }

        if (request.ExpenseHeader.DeliverymanId.HasValue)
        {
            var deliverymanId = request.ExpenseHeader.DeliverymanId.Value;
            var deliveryman = await _context.Users.FindAsync(new object[] { deliverymanId }, cancellationToken);
            if (deliveryman == null || deliveryman.Role != UserRole.Deliveryman || !deliveryman.Active)
            {
                throw new BusinessException("Domiciliario inválido");
            }

            if (deliveryman.BranchId != expenseHeader.BranchId)
            {
                throw new BusinessException("El domiciliario no pertenece a la sucursal del gasto");
            }

            expenseHeader.DeliverymanId = deliverymanId;
        }

        expenseHeader.Notes = NormalizeExpenseNote(request.ExpenseHeader.Notes, 2000);

        var newDetailInfos = new List<(int ExpenseId, decimal UnitAmount, int Quantity)>();

        // Manejar detalles: actualizar existentes, crear nuevos, eliminar los que no están en la lista
        if (request.ExpenseHeader.ExpenseDetails != null)
        {
            // Validar que los expenses existen
            var expenseIds = request.ExpenseHeader.ExpenseDetails.Select(ed => ed.ExpenseId).Distinct().ToList();
            var expenses = await _context.Expenses
                .AsNoTracking()
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
                expenseHeader.ExpenseDetails.Remove(detail);
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
                        existingDetail.Total = ExpenseInvoiceTotalsHelper.ResolveLineTotal(
                            detailDto.Quantity,
                            detailDto.Amount,
                            detailDto.Total);
                        existingDetail.IncludeVat = request.ExpenseHeader.IncludeVat || detailDto.IncludeVat;
                        existingDetail.Notes = NormalizeExpenseNote(detailDto.Notes, 1000);
                    }
                }
                else
                {
                    // Crear nuevo
                    var newDetail = new ExpenseDetail
                    {
                        ExpenseId = detailDto.ExpenseId,
                        Quantity = detailDto.Quantity,
                        Amount = detailDto.Amount,
                        Total = ExpenseInvoiceTotalsHelper.ResolveLineTotal(
                            detailDto.Quantity,
                            detailDto.Amount,
                            detailDto.Total),
                        IncludeVat = request.ExpenseHeader.IncludeVat || detailDto.IncludeVat,
                        Notes = NormalizeExpenseNote(detailDto.Notes, 1000),
                    };
                    expenseHeader.ExpenseDetails.Add(newDetail);
                    newDetailInfos.Add((newDetail.ExpenseId, (decimal)newDetail.Amount, (int)Math.Ceiling(newDetail.Quantity)));
                }
            }
        }

        if (request.ExpenseHeader.IncludeVat)
        {
            foreach (var detail in expenseHeader.ExpenseDetails)
                detail.IncludeVat = true;
        }

        var subtotalForVat = ExpenseInvoiceTotalsHelper.SubtotalFromTrackedDetails(expenseHeader.ExpenseDetails);
        var taxableSubtotal = ExpenseInvoiceTotalsHelper.TaxableSubtotalFromTrackedDetails(expenseHeader.ExpenseDetails);
        expenseHeader.VatAmount = ExpenseInvoiceTotalsHelper.ComputeVatAmount(
            taxableSubtotal,
            taxableSubtotal > 0);

        // Manejar pagos: PUT reemplaza la lista completa; null equivale a sin pagos bancarios.
        var requestedBankPayments = request.ExpenseHeader.ExpenseBankPayments ?? new List<CreateExpenseBankPaymentDto>();

        // Primero eliminar los pagos actuales
        var existingPayments = await _context.ExpenseBankPayments
            .Where(p => p.ExpenseHeaderId == expenseHeader.Id)
            .ToListAsync(cancellationToken);
        if (existingPayments.Any())
        {
            _context.ExpenseBankPayments.RemoveRange(existingPayments);
            expenseHeader.ExpenseBankPayments.Clear();
        }

        if (requestedBankPayments.Any())
        {
            var branchId = _currentUser.BranchId;
            var bankIds = requestedBankPayments.Select(ebp => ebp.BankId).Distinct().ToList();
            var banks = await _context.Banks
                .Where(b => bankIds.Contains(b.Id) && b.BranchId == branchId)
                .ToListAsync(cancellationToken);

            if (banks.Count != bankIds.Count)
            {
                var foundBankIds = banks.Select(b => b.Id).ToList();
                var missingBankIds = bankIds.Except(foundBankIds).ToList();
                throw new NotFoundException($"Bancos con IDs {string.Join(", ", missingBankIds)} no encontrados o no pertenecen a la sucursal");
            }

            foreach (var paymentDto in requestedBankPayments)
            {
                expenseHeader.ExpenseBankPayments.Add(new ExpenseBankPayment
                {
                    BankId = paymentDto.BankId,
                    Amount = paymentDto.Amount
                });
            }
        }

        var totalBankPayments = expenseHeader.ExpenseBankPayments.Sum(p => p.Amount);
        var grossTotal = ExpenseInvoiceTotalsHelper.GrossTotal(subtotalForVat, expenseHeader.VatAmount);
        if (totalBankPayments > grossTotal)
            throw new BusinessException("La suma de pagos bancarios no puede exceder el total de la factura (incluye IVA si aplica)");

        expenseHeader.Total = grossTotal;

        var updated = await _expenseHeaderRepository.UpdateAsync(expenseHeader, cancellationToken);

        await SyncLinkedDeliverymanAdvanceAmountAsync(updated.Id, updated.Total ?? 0, cancellationToken);

        if (newDetailInfos.Any())
        {
            await UpsertSupplierExpensesAsync(expenseHeader.SupplierId, newDetailInfos, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        var updatedWithDetails = await _expenseHeaderRepository.GetByIdWithDetailsAsync(updated.Id, cancellationToken);

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

        await ExpenseHeaderLinkedAdvancePopulator.PopulateAsync(_context, new[] { dto }, cancellationToken);

        return dto;
    }

    private async Task SyncLinkedDeliverymanAdvanceAmountAsync(
        int expenseHeaderId,
        decimal newTotal,
        CancellationToken cancellationToken)
    {
        var linkedAdvance = await _context.DeliverymanAdvances
            .FirstOrDefaultAsync(a => a.ExpenseHeaderId == expenseHeaderId, cancellationToken);
        if (linkedAdvance == null || linkedAdvance.Amount == newTotal)
            return;

        if (!ColombiaTimeHelper.IsColombiaTodayFromUtc(linkedAdvance.CreatedAt, _clock.UtcNow))
        {
            throw new BusinessException(
                "El abono vinculado a este gasto solo puede ajustarse automáticamente el mismo día de su registro (hora Colombia). " +
                "Para otro día, actualiza el abono en el módulo de domiciliarios.");
        }

        linkedAdvance.Amount = newTotal;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertSupplierExpensesAsync(
        int supplierId,
        IEnumerable<(int ExpenseId, decimal UnitAmount, int Quantity)> detailInfos,
        CancellationToken cancellationToken)
    {
        var items = detailInfos.ToList();
        if (!items.Any())
        {
            return;
        }

        var expenseIds = items.Select(i => i.ExpenseId).Distinct().ToList();
        var existing = await _context.SupplierExpenses
            .Where(se => se.SupplierId == supplierId && expenseIds.Contains(se.ExpenseId))
            .ToListAsync(cancellationToken);

        var now = _clock.UtcNow;

        foreach (var item in items)
        {
            var supplierExpense = existing.FirstOrDefault(se => se.ExpenseId == item.ExpenseId);
            if (supplierExpense == null)
            {
                supplierExpense = new SupplierExpense
                {
                    SupplierId = supplierId,
                    ExpenseId = item.ExpenseId,
                    UsageCount = item.Quantity,
                    LastUsedAt = now,
                    LastUnitPrice = item.UnitAmount
                };
                _context.SupplierExpenses.Add(supplierExpense);
                existing.Add(supplierExpense);
            }
            else
            {
                supplierExpense.UsageCount += item.Quantity;
                supplierExpense.LastUsedAt = now;
                supplierExpense.LastUnitPrice = item.UnitAmount;
            }
        }
    }

    private static string? NormalizeExpenseNote(string? notes, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;
        var t = notes.Trim();
        return t.Length <= maxLen ? t : t[..maxLen];
    }
}

