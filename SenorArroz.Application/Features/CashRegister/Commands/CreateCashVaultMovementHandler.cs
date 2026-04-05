using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Application.Features.CashRegister.Queries;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Enums;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CreateCashVaultMovementHandler : IRequestHandler<CreateCashVaultMovementCommand, CashVaultMovementDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;

    public CreateCashVaultMovementHandler(
        IApplicationDbContext context,
        ICurrentUser currentUser,
        IMediator mediator)
    {
        _context = context;
        _currentUser = currentUser;
        _mediator = mediator;
    }

    public async Task<CashVaultMovementDto> Handle(CreateCashVaultMovementCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
            throw new BusinessException("Usuario no autenticado");

        var branchId = request.BranchId ?? _currentUser.BranchId;
        if (branchId <= 0)
            throw new BusinessException("Sucursal no válida");

        var dto = request.Dto ?? throw new BusinessException("Datos no válidos");

        var vaultBank = await _context.Banks
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BranchId == branchId && b.Type == BankType.CashVault && b.Active, cancellationToken);

        if (vaultBank == null)
            throw new BusinessException("No hay banco «Caja Mayor Efectivo» activo en esta sucursal");

        if (_currentUser.Role != "superadmin" && _currentUser.Role != "admin")
            throw new BusinessException("Solo administradores pueden registrar abonos o descargas de caja mayor");

        if (_currentUser.Role != "superadmin" && vaultBank.BranchId != _currentUser.BranchId)
            throw new BusinessException("No tienes permiso para esta sucursal");

        decimal amount;

        if (dto.Kind == CashVaultMovementKind.AbonoToVault)
        {
            if (dto.WithdrawAll)
                throw new BusinessException("«Descargar todo» solo aplica a descargas");

            amount = dto.Amount ?? throw new BusinessException("Indica el monto del abono");
            if (amount <= 0)
                throw new BusinessException("El monto debe ser mayor a 0");
        }
        else if (dto.Kind == CashVaultMovementKind.WithdrawFromVault)
        {
            var expected = await _mediator.Send(new GetCashRegisterExpectedQuery { BranchId = branchId }, cancellationToken);
            var row = expected.Banks.FirstOrDefault(b => b.BankId == vaultBank.Id);
            var expectedVault = row?.ExpectedBalance ?? 0;

            if (dto.WithdrawAll)
                amount = expectedVault;
            else
                amount = dto.Amount ?? throw new BusinessException("Indica el monto de la descarga o usa «descargar todo»");

            if (amount <= 0)
                throw new BusinessException("No hay saldo esperado para descargar en Caja Mayor Efectivo");

            if (amount > expectedVault)
                throw new BusinessException($"El monto supera el saldo esperado en Caja Mayor Efectivo ({expectedVault:N0} $)");
        }
        else
        {
            throw new BusinessException("Tipo de movimiento no válido");
        }

        if (!string.IsNullOrEmpty(dto.Note) && dto.Note.Length > 500)
            throw new BusinessException("La nota no puede superar 500 caracteres");

        var entity = new CashVaultMovement
        {
            BranchId = branchId,
            BankId = vaultBank.Id,
            Kind = dto.Kind,
            Amount = amount,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
            CreatedById = _currentUser.Id,
        };

        _context.CashVaultMovements.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return new CashVaultMovementDto
        {
            Id = entity.Id,
            BranchId = entity.BranchId,
            BankId = entity.BankId,
            BankName = vaultBank.Name,
            Kind = entity.Kind,
            Amount = entity.Amount,
            Note = entity.Note,
            CreatedAt = entity.CreatedAt,
            CreatedById = entity.CreatedById,
        };
    }
}
