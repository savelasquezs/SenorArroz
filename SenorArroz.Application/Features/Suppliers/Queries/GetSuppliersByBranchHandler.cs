using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSuppliersByBranchHandler : IRequestHandler<GetSuppliersByBranchQuery, List<SupplierDto>>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetSuppliersByBranchHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<List<SupplierDto>> Handle(GetSuppliersByBranchQuery request, CancellationToken cancellationToken)
    {
        int branchId;

        if (_currentUser.Role == "superadmin")
        {
            if (!request.BranchId.HasValue || request.BranchId <= 0)
            {
                throw new BusinessException("Debes especificar la sucursal para obtener los proveedores.");
            }
            branchId = request.BranchId.Value;
        }
        else
        {
            branchId = _currentUser.BranchId;
            if (branchId <= 0)
            {
                throw new BusinessException("El usuario no tiene una sucursal asociada.");
            }
        }

        var suppliers = await _supplierRepository.GetByBranchAsync(branchId);
        return _mapper.Map<List<SupplierDto>>(suppliers);
    }
}


