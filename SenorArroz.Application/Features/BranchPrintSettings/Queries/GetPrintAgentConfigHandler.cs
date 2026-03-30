using AutoMapper;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BranchPrintSettings.DTOs;

namespace SenorArroz.Application.Features.BranchPrintSettings.Queries;

public class GetPrintAgentConfigHandler : IRequestHandler<GetPrintAgentConfigQuery, PrintAgentConfigDto?>
{
    private readonly IApplicationDbContext _db;
    private readonly IMapper _mapper;

    public GetPrintAgentConfigHandler(IApplicationDbContext db, IMapper mapper)
    {
        _db = db;
        _mapper = mapper;
    }

    public async Task<PrintAgentConfigDto?> Handle(GetPrintAgentConfigQuery request, CancellationToken cancellationToken)
    {
        var entity = await _db.BranchPrintSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == request.BranchId, cancellationToken);
        if (entity is null)
            return null;

        return _mapper.Map<PrintAgentConfigDto>(entity);
    }
}
