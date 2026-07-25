// SenorArroz.Application/Features/Apps/Queries/GetAppsByBankHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Apps.Queries;

public class GetAppsByBankHandler : IRequestHandler<GetAppsByBankQuery, IEnumerable<AppDto>>
{
    private readonly IAppRepository _appRepository;
    private readonly IMapper _mapper;
    private readonly IBranchContext _branchContext;

    public GetAppsByBankHandler(
        IAppRepository appRepository,
        IMapper mapper,
        IBranchContext branchContext)
    {
        _appRepository = appRepository;
        _mapper = mapper;
        _branchContext = branchContext;
    }

    public async Task<IEnumerable<AppDto>> Handle(GetAppsByBankQuery request, CancellationToken cancellationToken)
    {
        var apps = await _appRepository.GetByBankIdAsync(request.BankId, cancellationToken);
        
        var appDtos = new List<AppDto>();

        var branchId = _branchContext.RequireBranch();
        foreach (var app in apps.Where(x => x.Bank.BranchId == branchId))
        {
            var appDto = _mapper.Map<AppDto>(app);
            appDtos.Add(appDto);
        }

        return appDtos;
    }
}
