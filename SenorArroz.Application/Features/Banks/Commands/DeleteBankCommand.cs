// SenorArroz.Application/Features/Banks/Commands/DeleteBankCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.Banks.Commands;

public class DeleteBankCommand : IRequest<bool>
{
    public int Id { get; set; }
}
