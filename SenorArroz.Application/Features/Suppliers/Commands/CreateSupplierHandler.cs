using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Entities;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Commands;

public class CreateSupplierHandler : IRequestHandler<CreateSupplierCommand, SupplierDto>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateSupplierHandler(
        ISupplierRepository supplierRepository,
        IBranchRepository branchRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _branchRepository = branchRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<SupplierDto> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        // Permissions: Superadmin/Admin/Cashier pueden crear proveedores
        if (_currentUser.Role is not ("superadmin" or "admin" or "cashier"))
        {
            throw new BusinessException("No tienes permisos para crear proveedores.");
        }

        var branchId = await ResolveBranchIdAsync(request.BranchId);

        // Validaciones de unicidad
        if (await _supplierRepository.NameExistsAsync(request.Supplier.Name, branchId))
        {
            throw new BusinessException("Ya existe un proveedor con ese nombre en la sucursal.");
        }

        if (await _supplierRepository.PhoneExistsAsync(request.Supplier.Phone, branchId))
        {
            throw new BusinessException("Ya existe un proveedor con ese teléfono en la sucursal.");
        }

        var supplier = new Supplier
        {
            BranchId = branchId,
            Name = request.Supplier.Name.Trim(),
            Phone = request.Supplier.Phone.Trim(),
            Address = request.Supplier.Address,
            Email = request.Supplier.Email
        };

        var created = await _supplierRepository.CreateAsync(supplier);
        return _mapper.Map<SupplierDto>(created);
    }

    private async Task<int> ResolveBranchIdAsync(int? requestedBranchId)
    {
        if (_currentUser.Role == "superadmin")
        {
            if (!requestedBranchId.HasValue || requestedBranchId <= 0)
            {
                throw new BusinessException("Superadmin debe especificar la sucursal.");
            }

            if (!await _branchRepository.ExistsAsync(requestedBranchId.Value))
            {
                throw new BusinessException("La sucursal indicada no existe.");
            }

            return requestedBranchId.Value;
        }

        if (_currentUser.BranchId <= 0)
        {
            throw new BusinessException("El usuario no tiene una sucursal asociada.");
        }

        return _currentUser.BranchId;
    }
}


