// SenorArroz.Application/Features/BankPayments/Queries/GetBankPaymentByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.BankPayments.DTOs;

namespace SenorArroz.Application.Features.BankPayments.Queries;

public class GetBankPaymentByIdQuery : IRequest<BankPaymentDto?>
{
    public int Id { get; set; }
}
