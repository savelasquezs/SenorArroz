// SenorArroz.Application/Features/Apps/Commands/DeleteAppCommand.cs
using MediatR;

namespace SenorArroz.Application.Features.Apps.Commands;

public class DeleteAppCommand : IRequest<bool>
{
    public int Id { get; set; }
}
