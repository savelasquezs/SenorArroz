// SenorArroz.Application/Features/Apps/Commands/UpdateAppCommand.cs
using MediatR;
using SenorArroz.Application.Features.Apps.DTOs;

namespace SenorArroz.Application.Features.Apps.Commands;

public class UpdateAppCommand : IRequest<AppDto>
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool Active { get; set; } = true;
}
