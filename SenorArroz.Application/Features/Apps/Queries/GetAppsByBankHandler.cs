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
    private readonly ICurrentUser _currentUser;

    public GetAppsByBankHandler(IAppRepository appRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _appRepository = appRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<AppDto>> Handle(GetAppsByBankQuery request, CancellationToken cancellationToken)
    {
        var apps = await _appRepository.GetByBankIdAsync(request.BankId);
        
        var appDtos = new List<AppDto>();

        foreach (var app in apps)
        {
            // Check if user has access to this app's branch
            if (_currentUser.Role != "superadmin" && app.Bank.BranchId != _currentUser.BranchId)
                continue;

            var appDto = _mapper.Map<AppDto>(app);
            appDtos.Add(appDto);
        }

        return appDtos;
    }
}
