using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Commands;

public class UpdateNeighborhoodCommand : IRequest<BranchNeighborhoodDto>
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveryFee { get; set; }
}