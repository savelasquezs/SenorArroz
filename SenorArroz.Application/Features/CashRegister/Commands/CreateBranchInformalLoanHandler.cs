using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.CashRegister.DTOs;
using SenorArroz.Domain.Entities;

namespace SenorArroz.Application.Features.CashRegister.Commands;

public class CreateBranchInformalLoanHandler : IRequestHandler<CreateBranchInformalLoanCommand, BranchInformalLoanDto>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUser _currentUser;

    public CreateBranchInformalLoanHandler(IApplicationDbContext context, ICurrentUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<BranchInformalLoanDto> Handle(CreateBranchInformalLoanCommand request, CancellationToken cancellationToken)
    {
        var dto = request.Dto;
        if (string.IsNullOrWhiteSpace(dto.Concept))
            throw new InvalidOperationException("El concepto es obligatorio.");

        int branchId = request.BranchId ?? _currentUser.BranchId;

        var entity = new BranchInformalLoan
        {
            BranchId = branchId,
            Concept = dto.Concept.Trim(),
            Amount = dto.Amount,
            CreatedById = _currentUser.Id
        };

        _context.BranchInformalLoans.Add(entity);
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
