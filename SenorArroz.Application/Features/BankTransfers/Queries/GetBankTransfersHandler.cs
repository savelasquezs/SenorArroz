using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BankTransfers.Queries;

public class GetBankTransfersHandler : IRequestHandler<GetBankTransfersQuery, PagedResult<BankTransferDto>>
{
    private readonly IBankTransferRepository _bankTransferRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetBankTransfersHandler(
        IBankTransferRepository bankTransferRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _bankTransferRepository = bankTransferRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<BankTransferDto>> Handle(GetBankTransfersQuery request, CancellationToken cancellationToken)
    {
        int? branchFilter = null;
        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId.HasValue && request.BranchId > 0)
        {
            branchFilter = request.BranchId;
        }

        var result = await _bankTransferRepository.GetPagedAsync(
            branchFilter,
            request.FromBankId,
            request.ToBankId,
            request.FromDate,
            request.ToDate,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var dtos = _mapper.Map<List<BankTransferDto>>(result.Items);
        return new PagedResult<BankTransferDto>
        {
            Items = dtos,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }
}
