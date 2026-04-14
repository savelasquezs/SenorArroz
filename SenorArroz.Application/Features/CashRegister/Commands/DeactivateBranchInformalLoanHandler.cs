using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class DeactivateBranchInformalLoanHandler : IRequestHandler<DeactivateBranchInformalLoanCommand, BranchInformalLoanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;

    public DeactivateBranchInformalLoanHandler(IApplicationDbContext context, ICurrentUser currentUser, IClock clock)
    {
        _context = context;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<BranchInformalLoanDto> Handle(DeactivateBranchInformalLoanCommand request, CancellationToken cancellationToken)
    {
        int branchId = request.BranchId ?? _currentUser.BranchId;

        var entity = await _context.BranchInformalLoans
            .FirstOrDefaultAsync(l => l.Id == request.Id && l.BranchId == branchId, cancellationToken);

        if (entity is null)
            throw new InvalidOperationException("Préstamo no encontrado.");

        if (entity.DeactivatedAt != null)
            throw new InvalidOperationException("Este préstamo ya fue dado de baja.");

        entity.DeactivatedAt = _clock.UtcNow;
        entity.DeactivatedById = _currentUser.Id;
        entity.DeactivationNotes = string.IsNullOrWhiteSpace(request.Dto.Notes)
            ? null
            : request.Dto.Notes.Trim();

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
