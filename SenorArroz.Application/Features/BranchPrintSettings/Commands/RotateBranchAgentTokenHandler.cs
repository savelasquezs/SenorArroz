using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Common.Printing;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public class RotateBranchAgentTokenHandler : IRequestHandler<RotateBranchAgentTokenCommand, RotateBranchAgentTokenResponseDto>
{
    private readonly IApplicationDbContext _db;

    public RotateBranchAgentTokenHandler(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<RotateBranchAgentTokenResponseDto> Handle(RotateBranchAgentTokenCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.BranchPrintSettings
            .FirstOrDefaultAsync(s => s.BranchId == request.BranchId, cancellationToken);
        if (entity is null)
            throw new NotFoundException($"No hay configuración de impresión para la sucursal {request.BranchId}.");

        var plainToken = PrintAgentTokenCrypto.NewPlainToken();
        var salt = PrintAgentTokenCrypto.NewSalt();
        entity.AgentTokenSalt = salt;
        entity.AgentTokenHash = PrintAgentTokenCrypto.ComputeHash(salt, plainToken);
        entity.AgentTokenUpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        return new RotateBranchAgentTokenResponseDto { PlainToken = plainToken };
    }
}
