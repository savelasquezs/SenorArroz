using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Commands;

public class CreateBranchCommand : IRequest<BranchDto>
{
    public string Name { get; set; } = string.Empty;
    public string? BusinessName { get; set; }
    public string? Nit { get; set; }
    public string Address { get; set; } = string.Empty;
    public string Phone1 { get; set; } = string.Empty;
    public string? Phone2 { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}