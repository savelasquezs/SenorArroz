using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class UpdateBranchHandler : IRequestHandler<UpdateBranchCommand, BranchDto>
{
    private readonly IBranchRepository _branchRepository;
    private readonly IMapper _mapper;

    public UpdateBranchHandler(IBranchRepository branchRepository, IMapper mapper)
    {
        _branchRepository = branchRepository;
        _mapper = mapper;
    }

    public async Task<BranchDto> Handle(UpdateBranchCommand request, CancellationToken cancellationToken)
    {
        var branch = await _branchRepository.GetByIdAsync(request.Id);
        if (branch == null)
        {
            throw new NotFoundException($"Sucursal con ID {request.Id} no encontrada");
        }

        // Validate name doesn't exist for other branches
        if (await _branchRepository.NameExistsAsync(request.Name, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el nombre '{request.Name}'");
        }

        // Validate phone doesn't exist for other branches
        if (await _branchRepository.PhoneExistsAsync(request.Phone1, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el teléfono {request.Phone1}");
        }

        if (!string.IsNullOrEmpty(request.Phone2) && await _branchRepository.PhoneExistsAsync(request.Phone2, request.Id))
        {
            throw new BusinessException($"Ya existe otra sucursal con el teléfono {request.Phone2}");
        }

        // Update branch
        branch.Name = request.Name.Trim();
        branch.Address = request.Address.Trim();
        branch.Phone1 = request.Phone1;
        branch.Phone2 = request.Phone2;

        branch = await _branchRepository.UpdateAsync(branch);

        var branchDto = _mapper.Map<BranchDto>(branch);

        // Add current statistics
        branchDto.TotalUsers = await _branchRepository.GetTotalUsersAsync(branch.Id);
        branchDto.ActiveUsers = await _branchRepository.GetActiveUsersAsync(branch.Id);
        branchDto.TotalCustomers = await _branchRepository.GetTotalCustomersAsync(branch.Id);
        branchDto.ActiveCustomers = await _branchRepository.GetActiveCustomersAsync(branch.Id);
        branchDto.TotalNeighborhoods = await _branchRepository.GetTotalNeighborhoodsAsync(branch.Id);

        return branchDto;
    }
}