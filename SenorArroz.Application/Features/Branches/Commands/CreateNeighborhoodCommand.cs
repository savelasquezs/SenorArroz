using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;

namespace SenorArroz.Application.Features.Branches.Commands;

public class CreateNeighborhoodCommand : IRequest<BranchNeighborhoodDto>
{
    public int BranchId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DeliveryFee { get; set; }
}