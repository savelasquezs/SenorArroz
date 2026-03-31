using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public class DeleteBranchReceiptLogoHandler : IRequestHandler<DeleteBranchReceiptLogoCommand, BranchPrintSettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IBranchReceiptLogoStorage _storage;
    private readonly IMapper _mapper;

    public DeleteBranchReceiptLogoHandler(
        IApplicationDbContext db,
        IBranchReceiptLogoStorage storage,
        IMapper mapper)
    {
        _db = db;
        _storage = storage;
        _mapper = mapper;
    }

    public async Task<BranchPrintSettingsDto> Handle(DeleteBranchReceiptLogoCommand request, CancellationToken cancellationToken)
    {
        var entity = await _db.BranchPrintSettings
            .FirstOrDefaultAsync(s => s.BranchId == request.BranchId, cancellationToken);
        if (entity is null)
            throw new NotFoundException($"No hay configuración de impresión para la sucursal {request.BranchId}.");

        await _storage.ClearAsync(request.BranchId, cancellationToken);
        entity.ReceiptLogoPath = null;
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BranchPrintSettingsDto>(entity);
    }
}
