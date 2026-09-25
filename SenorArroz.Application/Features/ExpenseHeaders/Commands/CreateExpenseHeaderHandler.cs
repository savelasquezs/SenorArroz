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

public class CreateExpenseHeaderHandler : IRequestHandler<CreateExpenseHeaderCommand, ExpenseHeaderDto>
{
    private readonly IExpenseHeaderRepository _expenseHeaderRepository;
    private readonly IBankRepository _bankRepository;
    private readonly IApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;
    private readonly IClock _clock;
    private readonly IInventoryService? _inventory;

    public CreateExpenseHeaderHandler(
        IExpenseHeaderRepository expenseHeaderRepository,
        IBankRepository bankRepository,
        IApplicationDbContext context,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext,
        IClock clock,
        IInventoryService? inventory = null)
    {
        _expenseHeaderRepository = expenseHeaderRepository;
        _bankRepository = bankRepository;
        _context = context;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
        _clock = clock;
        _inventory = inventory;
    }

    public async Task<ExpenseHeaderDto> Handle(CreateExpenseHeaderCommand request, CancellationToken cancellationToken)
    {
        var operationKey = NormalizeOperationKey(request.ExpenseHeader.IdempotencyKey) ?? Guid.NewGuid().ToString("N");
        var branchId = _branchContext.RequireBranch();
        var existingHeaderId = await _context.ExpenseHeaders.AsNoTracking()
            .Where(x => x.BranchId == branchId && x.InventoryOperationKey == operationKey)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingHeaderId.HasValue)
        {
            var existingHeader = await _expenseHeaderRepository.GetByIdWithDetailsAsync(existingHeaderId.Value, cancellationToken);
            return _mapper.Map<ExpenseHeaderDto>(existingHeader!);
        }
        // Validar que el supplier existe
        var supplier = await _context.Suppliers.FindAsync(new object[] { request.ExpenseHeader.SupplierId }, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException($"Proveedor con ID {request.ExpenseHeader.SupplierId} no encontrado");
        }

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

        var subtotal = ExpenseInvoiceTotalsHelper.SubtotalFromCreateDetails(request.ExpenseHeader.ExpenseDetails);
        var taxableSubtotal = ExpenseInvoiceTotalsHelper.TaxableSubtotalFromCreateDetails(
            request.ExpenseHeader.ExpenseDetails,
            request.ExpenseHeader.IncludeVat);
        var vatAmount = ExpenseInvoiceTotalsHelper.ComputeVatAmount(taxableSubtotal, taxableSubtotal > 0);
        var grossTotal = ExpenseInvoiceTotalsHelper.GrossTotal(subtotal, vatAmount);

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

            var totalBankPayments = request.ExpenseHeader.ExpenseBankPayments.Sum(ebp => (decimal)ebp.Amount);
            if (totalBankPayments > grossTotal)
                throw new BusinessException("La suma de pagos bancarios no puede exceder el total de la factura (incluye IVA si aplica)");
        }

        if (request.ExpenseHeader.DeliverymanId.HasValue)
        {
            var dm = await _context.Users.FindAsync(new object[] { request.ExpenseHeader.DeliverymanId.Value }, cancellationToken);
            if (dm == null || dm.Role != UserRole.Deliveryman || !dm.Active)
                throw new BusinessException("Domiciliario inválido");
            if (dm.BranchId != branchId)
                throw new BusinessException("El domiciliario no pertenece a tu sucursal");
        }

        // Crear ExpenseHeader
        var expenseHeader = new ExpenseHeader
        {
            BranchId = branchId,
            SupplierId = request.ExpenseHeader.SupplierId,
            CreatedById = _currentUser.Id,
            DeliverymanId = request.ExpenseHeader.DeliverymanId,
            VatAmount = vatAmount,
            Total = grossTotal,
            Notes = NormalizeExpenseNote(request.ExpenseHeader.Notes, 2000),
            InventoryOperationKey = operationKey,
            ExpenseDetails = request.ExpenseHeader.ExpenseDetails.Select(ed => new ExpenseDetail
            {
                ExpenseId = ed.ExpenseId,
                Quantity = ed.Quantity,
                Amount = ed.Amount,
                Total = ExpenseInvoiceTotalsHelper.ResolveLineTotal(ed.Quantity, ed.Amount, ed.Total),
                IncludeVat = request.ExpenseHeader.IncludeVat || ed.IncludeVat,
                Notes = NormalizeExpenseNote(ed.Notes, 1000),
                InventoryConversionId = ed.InventoryConversionId,
            }).ToList(),
            ExpenseBankPayments = request.ExpenseHeader.ExpenseBankPayments?.Select(ebp => new ExpenseBankPayment
            {
                BankId = ebp.BankId,
                Amount = ebp.Amount
            }).ToList() ?? new List<ExpenseBankPayment>()
        };

        await using var transaction = _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
        var created = await _expenseHeaderRepository.CreateAsync(expenseHeader, cancellationToken);
        if (_inventory is not null)
            await _inventory.RecordPurchaseAsync(expenseHeader, $"expense-header:{operationKey}:create", cancellationToken);

        var supplierExpenseDetails = expenseHeader.ExpenseDetails
            .Select(ed => (ed.ExpenseId, UnitAmount: (decimal)ed.Amount, Quantity: (int)Math.Ceiling(ed.Quantity)))
            .ToList();
        await UpsertSupplierExpensesAsync(expenseHeader.SupplierId, supplierExpenseDetails, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var createdWithDetails = await _expenseHeaderRepository.GetByIdWithDetailsAsync(created.Id, cancellationToken);

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

        await ExpenseHeaderLinkedAdvancePopulator.PopulateAsync(_context, new[] { dto }, cancellationToken);

        return dto;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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

    private static string? NormalizeOperationKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > 120) throw new BusinessException("La clave de idempotencia no puede superar 120 caracteres");
        return normalized;
    }
}

