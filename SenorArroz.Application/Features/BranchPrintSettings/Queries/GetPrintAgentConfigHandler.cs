using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SenorArroz.Application.Common;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;
using SenorArroz.Application.Options;

namespace SenorArroz.Application.Features.BranchPrintSettings.Queries;

public class GetPrintAgentConfigHandler : IRequestHandler<GetPrintAgentConfigQuery, PrintAgentConfigDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly IOptions<ApiPublicOptions> _apiPublic;

    public GetPrintAgentConfigHandler(
        IApplicationDbContext db,
        IMapper mapper,
        IOptions<ApiPublicOptions> apiPublic)
    {
        _db = db;
        _mapper = mapper;
        _apiPublic = apiPublic;
    }

    public async Task<PrintAgentConfigDto?> Handle(GetPrintAgentConfigQuery request, CancellationToken cancellationToken)
    {
        var entity = await _db.BranchPrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == request.BranchId, cancellationToken);
        if (entity is null)
            return null;

        var dto = _mapper.Map<PrintAgentConfigDto>(entity);
        var baseUrl = _apiPublic.Value.BaseUrl;
        dto.ReceiptLogoUrl = PublicUrlHelper.ToAbsolutePublicUrl(baseUrl, entity.ReceiptLogoPath);
        return dto;
    }
}
