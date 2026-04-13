// SenorArroz.Application/Features/BankPayments/Queries/GetBankPaymentsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Helpers;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.BankPayments.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.BankPayments.Queries;

public class GetBankPaymentsHandler : IRequestHandler<GetBankPaymentsQuery, PagedResult<BankPaymentDto>>
{
    private readonly IBankPaymentRepository _bankPaymentRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetBankPaymentsHandler(IBankPaymentRepository bankPaymentRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _bankPaymentRepository = bankPaymentRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<BankPaymentDto>> Handle(GetBankPaymentsQuery request, CancellationToken cancellationToken)
    {
        int? restrictBranch = !Roles.IsSuperadmin(_currentUser.Role)
            ? _currentUser.BranchId
            : request.BranchId;

        var (fromUtc, toUtc) = ColombiaTimeHelper.NormalizeApiDateFiltersToUtc(request.FromDate, request.ToDate);

        var pagedBankPayments = await _bankPaymentRepository.GetPagedAsync(
            request.OrderId,
            request.BankId,
            request.Verified,
            fromUtc,
            toUtc,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            restrictToBankBranchId: restrictBranch);

        var bankPaymentDtos = pagedBankPayments.Items
            .Select(bp => _mapper.Map<BankPaymentDto>(bp))
            .ToList();

        return new PagedResult<BankPaymentDto>
        {
            Items = bankPaymentDtos,
            TotalCount = pagedBankPayments.TotalCount,
            Page = pagedBankPayments.Page,
            PageSize = pagedBankPayments.PageSize,
            TotalPages = pagedBankPayments.TotalPages
        };
    }
}
