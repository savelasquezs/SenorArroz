// SenorArroz.Application/Features/Banks/Queries/GetBankDetailQuery.cs
using MediatR;
using SenorArroz.Application.Features.Banks.DTOs;

namespace SenorArroz.Application.Features.Banks.Queries;

public class GetBankDetailQuery : IRequest<BankDetailDto?>
{
    public int Id { get; set; }
}
