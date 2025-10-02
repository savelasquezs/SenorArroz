// SenorArroz.Application/Features/BankPayments/Queries/GetBankPaymentsHandler.cs
using AutoMapper;
using MediatR;
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
        var pagedBankPayments = await _bankPaymentRepository.GetPagedAsync(
            request.OrderId,
            request.BankId,
            request.Verified,
            request.FromDate,
            request.ToDate,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

        var bankPaymentDtos = new List<BankPaymentDto>();

        foreach (var bankPayment in pagedBankPayments.Items)
        {
            // Check if user has access to this bank payment's branch
            if (_currentUser.Role != "superadmin" && bankPayment.Bank.BranchId != _currentUser.BranchId)
                continue;

            var bankPaymentDto = _mapper.Map<BankPaymentDto>(bankPayment);
            bankPaymentDtos.Add(bankPaymentDto);
        }

        return new PagedResult<BankPaymentDto>
        {
            Items = bankPaymentDtos,
            TotalCount = bankPaymentDtos.Count,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalPages = (int)Math.Ceiling(bankPaymentDtos.Count / (double)request.PageSize)
        };
    }
}
