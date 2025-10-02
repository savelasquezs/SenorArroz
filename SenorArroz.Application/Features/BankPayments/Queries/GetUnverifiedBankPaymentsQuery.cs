// SenorArroz.Application/Features/BankPayments/Queries/GetUnverifiedBankPaymentsQuery.cs
using MediatR;
using SenorArroz.Application.Features.BankPayments.DTOs;

namespace SenorArroz.Application.Features.BankPayments.Queries;

public class GetUnverifiedBankPaymentsQuery : IRequest<IEnumerable<BankPaymentDto>>
{
}
