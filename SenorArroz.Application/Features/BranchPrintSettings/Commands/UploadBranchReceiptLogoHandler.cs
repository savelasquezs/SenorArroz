using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Domain.Exceptions;

namespace SenorArroz.Application.Features.BranchPrintSettings.Commands;

public class UploadBranchReceiptLogoHandler : IRequestHandler<UploadBranchReceiptLogoCommand, BranchPrintSettingsDto>
{
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif",
    };

    private readonly IApplicationDbContext _db;
    private readonly IBranchReceiptLogoStorage _storage;
    private readonly IMapper _mapper;

    public UploadBranchReceiptLogoHandler(
        IApplicationDbContext db,
        IBranchReceiptLogoStorage storage,
        IMapper mapper)
    {
        _db = db;
        _storage = storage;
        _mapper = mapper;
    }

    public async Task<BranchPrintSettingsDto> Handle(UploadBranchReceiptLogoCommand request, CancellationToken cancellationToken)
    {
        if (request.Content.Length == 0)
            throw new BusinessException("El archivo está vacío.");

        if (request.Content.Length > 1_572_864)
            throw new BusinessException("El logo no puede superar 1,5 MB.");

        var ext = NormalizeExtension(request.ExtensionWithDot);
        if (!AllowedExt.Contains(ext))
            throw new BusinessException("Formato no permitido. Use PNG, JPEG, WebP o GIF.");

        var entity = await _db.BranchPrintSettings
            .FirstOrDefaultAsync(s => s.BranchId == request.BranchId, cancellationToken);
        if (entity is null)
            throw new NotFoundException($"No hay configuración de impresión para la sucursal {request.BranchId}.");

        var relative = await _storage.SaveAndReplaceAsync(request.BranchId, request.Content, ext, cancellationToken);
        entity.ReceiptLogoPath = relative;
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BranchPrintSettingsDto>(entity);
    }

    private static string NormalizeExtension(string raw)
    {
        var e = (raw ?? ".png").Trim().ToLowerInvariant();
        if (!e.StartsWith('.'))
            e = "." + e;
        if (e == ".jpeg")
            e = ".jpg";
        return e;
    }
}
