// SenorArroz.Application/Features/Apps/Commands/CreateAppCommand.cs
using MediatR;
using SenorArroz.Application.Features.Apps.DTOs;

namespace SenorArroz.Application.Features.Apps.Commands;

public class CreateAppCommand : IRequest<AppDto>
{
    public int BankId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; } = true;
}
