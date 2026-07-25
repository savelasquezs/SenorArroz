// SenorArroz.Application/Features/AppPayments/Queries/GetAppPaymentsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.AppPayments.Queries;

public class GetAppPaymentsHandler : IRequestHandler<GetAppPaymentsQuery, PagedResult<AppPaymentDto>>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;
    private readonly IBranchContext _branchContext;

    public GetAppPaymentsHandler(
        IAppPaymentRepository appPaymentRepository,
        IMapper mapper,
        ICurrentUser currentUser,
        IBranchContext branchContext)
    {
        _appPaymentRepository = appPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
        _branchContext = branchContext;
    }

    public async Task<PagedResult<AppPaymentDto>> Handle(GetAppPaymentsQuery request, CancellationToken cancellationToken)
    {
        var branchFilter = _branchContext.ResolveOptional(request.BranchId);
        var pagedAppPayments = await _appPaymentRepository.GetPagedAsync(
            request.OrderId,
            request.AppId,
            request.Settled,
            request.FromDate,
            request.ToDate,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var appPaymentDtos = new List<AppPaymentDto>();

        foreach (var appPayment in pagedAppPayments.Items)
        {
            // Check if user has access to this app payment's branch
            if (branchFilter.HasValue && appPayment.App.Bank.BranchId != branchFilter.Value)
                continue;

            var appPaymentDto = _mapper.Map<AppPaymentDto>(appPayment);
            appPaymentDtos.Add(appPaymentDto);
        }

        return new PagedResult<AppPaymentDto>
        {
            Items = appPaymentDtos,
            TotalCount = appPaymentDtos.Count,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(appPaymentDtos.Count / (double)request.PageSize)
        };
    }
}
