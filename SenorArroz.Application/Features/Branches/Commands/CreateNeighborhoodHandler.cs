using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class CreateNeighborhoodHandler : IRequestHandler<CreateNeighborhoodCommand, BranchNeighborhoodDto>
{
    private readonly INeighborhoodRepository _neighborhoodRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMapper _mapper;

    public CreateNeighborhoodHandler(
        INeighborhoodRepository neighborhoodRepository,
        IBranchRepository branchRepository,
        ICurrentUser currentUser,
        IMapper mapper)
    {
        _neighborhoodRepository = neighborhoodRepository;
        _branchRepository = branchRepository;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<BranchNeighborhoodDto> Handle(CreateNeighborhoodCommand request, CancellationToken cancellationToken)
    {
        if (!Roles.IsSuperadminOrAdminOrCashier(_currentUser.Role))
            throw new BusinessException("No tienes permisos para crear barrios");

        if (!Roles.IsSuperadmin(_currentUser.Role) && _currentUser.BranchId != request.BranchId)
            throw new UnauthorizedAccessException("No puedes crear barrios en otra sucursal");

        // Validate branch exists
        if (!await _branchRepository.ExistsAsync(request.BranchId))
        {
            throw new NotFoundException($"Sucursal con ID {request.BranchId} no encontrada");
        }

        // Validate name doesn't exist in this branch
        if (await _neighborhoodRepository.NameExistsAsync(request.Name, request.BranchId))
        {
            throw new BusinessException($"Ya existe un barrio con el nombre '{request.Name}' en esta sucursal");
        }

        var neighborhood = new Neighborhood
        {
            BranchId = request.BranchId,
            Name = request.Name.Trim(),
            DeliveryFee = request.DeliveryFee
        };

        neighborhood = await _neighborhoodRepository.CreateAsync(neighborhood, cancellationToken);

        var neighborhoodDto = _mapper.Map<BranchNeighborhoodDto>(neighborhood);

        // Initialize stats for new neighborhood
        neighborhoodDto.TotalCustomers = 0;
        neighborhoodDto.TotalAddresses = 0;

        return neighborhoodDto;
    }
}
