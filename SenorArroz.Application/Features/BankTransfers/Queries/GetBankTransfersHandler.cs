using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankTransfers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BankTransfers.Queries;

public class GetBankTransfersHandler : IRequestHandler<GetBankTransfersQuery, PagedResult<BankTransferDto>>
{
    private readonly IBankTransferRepository _bankTransferRepository;
    private readonly IMapper _mapper;
    private readonly IBranchContext _branchContext;

    public GetBankTransfersHandler(
        IBankTransferRepository bankTransferRepository,
        IMapper mapper,
        IBranchContext branchContext)
    {
        _bankTransferRepository = bankTransferRepository;
        _mapper = mapper;
        _branchContext = branchContext;
    }

    public async Task<PagedResult<BankTransferDto>> Handle(GetBankTransfersQuery request, CancellationToken cancellationToken)
    {
        var branchFilter = _branchContext.RequireBranch(request.BranchId);

        var (fromUtc, toUtc) = ColombiaTimeHelper.NormalizeApiDateFiltersToUtc(request.FromDate, request.ToDate);

        var result = await _bankTransferRepository.GetPagedAsync(
            branchFilter,
            request.FromBankId,
            request.ToBankId,
            fromUtc,
            toUtc,
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
