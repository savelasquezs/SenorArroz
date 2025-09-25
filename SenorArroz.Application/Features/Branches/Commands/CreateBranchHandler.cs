using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Branches.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Branches.Commands;

public class CreateBranchHandler : IRequestHandler<CreateBranchCommand, BranchDto>
{
    private readonly IBranchRepository _branchRepository;
    private readonly IMapper _mapper;

    public CreateBranchHandler(IBranchRepository branchRepository, IMapper mapper)
    {
        _branchRepository = branchRepository;
        _mapper = mapper;
    }

    public async Task<BranchDto> Handle(CreateBranchCommand request, CancellationToken cancellationToken)
    {
        // Validate name doesn't exist
        if (await _branchRepository.NameExistsAsync(request.Name))
        {
            throw new BusinessException($"Ya existe una sucursal con el nombre '{request.Name}'");
        }

        // Validate phone doesn't exist
        if (await _branchRepository.PhoneExistsAsync(request.Phone1))
        {
            throw new BusinessException($"Ya existe una sucursal con el teléfono {request.Phone1}");
        }

        if (!string.IsNullOrEmpty(request.Phone2) && await _branchRepository.PhoneExistsAsync(request.Phone2))
        {
            throw new BusinessException($"Ya existe una sucursal con el teléfono {request.Phone2}");
        }

        var branch = new Branch
        {
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Phone1 = request.Phone1,
            Phone2 = request.Phone2
        };

        branch = await _branchRepository.CreateAsync(branch);

        var branchDto = _mapper.Map<BranchDto>(branch);

        // Initialize stats for new branch
        branchDto.TotalUsers = 0;
        branchDto.ActiveUsers = 0;
        branchDto.TotalCustomers = 0;
        branchDto.ActiveCustomers = 0;
        branchDto.TotalNeighborhoods = 0;

        return branchDto;
    }
}