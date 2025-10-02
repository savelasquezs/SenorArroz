// SenorArroz.Application/Features/Apps/Queries/GetAppsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Apps.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Apps.Queries;

public class GetAppsHandler : IRequestHandler<GetAppsQuery, PagedResult<AppDto>>
{
    private readonly IAppRepository _appRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetAppsHandler(IAppRepository appRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _appRepository = appRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<AppDto>> Handle(GetAppsQuery request, CancellationToken cancellationToken)
    {
        // Determine branch filter based on user role
        int? branchFilter = null;
        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId > 0)
        {
            // Superadmin can optionally filter by specific branch
            branchFilter = request.BranchId;
        }
        // If branchFilter is null, superadmin gets all apps from all branches

        var pagedApps = await _appRepository.GetPagedAsync(
            request.BankId,
            request.Name,
            request.Active,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var appDtos = new List<AppDto>();

        foreach (var app in pagedApps.Items)
        {
            var appDto = _mapper.Map<AppDto>(app);

            // Check if user has access to this app's branch
            if (_currentUser.Role != "superadmin" && app.Bank.BranchId != _currentUser.BranchId)
                continue;

            // Add additional data
            appDto.TotalPayments = await _appRepository.GetTotalAppPaymentsAsync(app.Id);
            appDto.UnsettledPayments = await _appRepository.GetUnsettledAppPaymentsAsync(app.Id);
            appDto.TotalPaymentsCount = await _appRepository.GetTotalAppPaymentsCountAsync(app.Id);
            appDto.UnsettledPaymentsCount = await _appRepository.GetUnsettledAppPaymentsCountAsync(app.Id);

            appDtos.Add(appDto);
        }

        return new PagedResult<AppDto>
        {
            Items = appDtos,
            TotalCount = appDtos.Count,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(appDtos.Count / (double)request.PageSize)
        };
    }
}
