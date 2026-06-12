using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class UpdateBranchInformalLoanHandler : IRequestHandler<UpdateBranchInformalLoanCommand, BranchInformalLoanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public UpdateBranchInformalLoanHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<BranchInformalLoanDto> Handle(UpdateBranchInformalLoanCommand request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;
        var concept = request.Dto.Concept?.Trim();

        if (string.IsNullOrWhiteSpace(concept))
            throw new InvalidOperationException("El concepto es obligatorio.");

        var entity = await _context.BranchInformalLoans
            .FirstOrDefaultAsync(l => l.Id == request.Id && l.BranchId == branchId, cancellationToken);

        if (entity is null)
            throw new InvalidOperationException("Prestamo no encontrado.");

        entity.Concept = concept;
        entity.Amount = request.Dto.Amount;

        await _context.SaveChangesAsync(cancellationToken);

        return await _context.BranchInformalLoans
            .AsNoTracking()
            .Where(l => l.Id == entity.Id)
            .Select(l => new BranchInformalLoanDto
            {
                Id = l.Id,
                BranchId = l.BranchId,
                Concept = l.Concept,
                Amount = l.Amount,
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt,
                CreatedById = l.CreatedById,
                CreatedByName = l.CreatedBy.Name,
                DeactivatedAt = l.DeactivatedAt,
                DeactivatedById = l.DeactivatedById,
                DeactivatedByName = l.DeactivatedBy != null ? l.DeactivatedBy.Name : null,
                DeactivationNotes = l.DeactivationNotes
            })
            .FirstAsync(cancellationToken);
    }
}
