// SenorArroz.Application/Features/Banks/Commands/UpdateBankCommand.cs
using MediatR;
using SenorArroz.Application.Features.Banks.DTOs;

namespace SenorArroz.Application.Features.Banks.Commands;

public class UpdateBankCommand : IRequest<BankDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; } = true;
}
