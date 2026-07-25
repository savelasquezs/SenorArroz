// SenorArroz.Application/Features/AppPayments/Queries/GetUnsettledAppPaymentsHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.AppPayments.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.AppPayments.Queries;

public class GetUnsettledAppPaymentsHandler : IRequestHandler<GetUnsettledAppPaymentsQuery, IEnumerable<AppPaymentDto>>
{
    private readonly IAppPaymentRepository _appPaymentRepository;
    private readonly IMapper _mapper;
    private readonly IBranchContext _branchContext;

    public GetUnsettledAppPaymentsHandler(
        IAppPaymentRepository appPaymentRepository,
        IMapper mapper,
        IBranchContext branchContext)
    {
        _appPaymentRepository = appPaymentRepository;
        _mapper = mapper;
        _branchContext = branchContext;
    }

    public async Task<IEnumerable<AppPaymentDto>> Handle(GetUnsettledAppPaymentsQuery request, CancellationToken cancellationToken)
    {
        IEnumerable<Domain.Entities.AppPayment> unsettledPayments;

        // Use appropriate repository method based on parameters
        if (request.AppId.HasValue && request.FromDate.HasValue && request.ToDate.HasValue)
        {
            // Get all unsettled payments and filter by app and date range
            var allUnsettled = await _appPaymentRepository.GetUnsettledAsync(cancellationToken);
            unsettledPayments = allUnsettled.Where(ap => ap.AppId == request.AppId.Value &&
                                                         ap.CreatedAt >= request.FromDate.Value &&
                                                         ap.CreatedAt <= request.ToDate.Value);
        }
        else if (request.AppId.HasValue)
        {
            unsettledPayments = await _appPaymentRepository.GetUnsettledByAppIdAsync(request.AppId.Value, cancellationToken);
        }
        else if (request.FromDate.HasValue && request.ToDate.HasValue)
        {
            unsettledPayments = await _appPaymentRepository.GetUnsettledByDateRangeAsync(
                request.FromDate.Value, request.ToDate.Value);
        }
        else
        {
            unsettledPayments = await _appPaymentRepository.GetUnsettledAsync(cancellationToken);
        }
        
        var appPaymentDtos = new List<AppPaymentDto>();

        var branchId = _branchContext.RequireBranch();
        foreach (var appPayment in unsettledPayments.Where(x => x.App.Bank.BranchId == branchId))
        {
            var appPaymentDto = _mapper.Map<AppPaymentDto>(appPayment);
            appPaymentDtos.Add(appPaymentDto);
        }

        return appPaymentDtos;
    }
}
