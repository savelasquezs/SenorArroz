// SenorArroz.Application/Features/Apps/Queries/GetAppsByBankQuery.cs
using MediatR;
using SenorArroz.Application.Features.Apps.DTOs;

namespace SenorArroz.Application.Features.Apps.Queries;

public class GetAppsByBankQuery : IRequest<IEnumerable<AppDto>>
{
    public int BankId { get; set; }
}
