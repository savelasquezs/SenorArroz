// SenorArroz.Application/Features/Banks/Queries/GetBankByIdQuery.cs
using MediatR;
using SenorArroz.Application.Features.Banks.DTOs;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankByIdQuery : IRequest<BankDto?>
{
    public int Id { get; set; }
}
