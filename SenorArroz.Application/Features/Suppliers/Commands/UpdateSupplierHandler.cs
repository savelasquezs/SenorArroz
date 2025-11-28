using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Commands;

public class UpdateSupplierHandler : IRequestHandler<UpdateSupplierCommand, SupplierDto>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public UpdateSupplierHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<SupplierDto> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        // Solo Admin y Superadmin pueden editar proveedores
        if (_currentUser.Role is not ("admin" or "superadmin"))
        {
            throw new BusinessException("No tienes permisos para actualizar proveedores.");
        }

        var supplier = await _supplierRepository.GetByIdAsync(request.Id)
            ?? throw new NotFoundException("Proveedor no encontrado.");

        if (_currentUser.Role != "superadmin" && supplier.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No puedes editar proveedores de otra sucursal.");
        }

        if (!string.IsNullOrWhiteSpace(request.Supplier.Name) &&
            !string.Equals(request.Supplier.Name.Trim(), supplier.Name, StringComparison.OrdinalIgnoreCase))
        {
            if (await _supplierRepository.NameExistsAsync(request.Supplier.Name, supplier.BranchId, supplier.Id))
            {
                throw new BusinessException("Ya existe un proveedor con ese nombre en la sucursal.");
            }
            supplier.Name = request.Supplier.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Supplier.Phone) &&
            !string.Equals(request.Supplier.Phone.Trim(), supplier.Phone, StringComparison.OrdinalIgnoreCase))
        {
            if (await _supplierRepository.PhoneExistsAsync(request.Supplier.Phone, supplier.BranchId, supplier.Id))
            {
                throw new BusinessException("Ya existe un proveedor con ese teléfono en la sucursal.");
            }
            supplier.Phone = request.Supplier.Phone.Trim();
        }

        if (request.Supplier.Address is not null)
        {
            supplier.Address = string.IsNullOrWhiteSpace(request.Supplier.Address)
                ? null
                : request.Supplier.Address;
        }

        if (request.Supplier.Email is not null)
        {
            supplier.Email = string.IsNullOrWhiteSpace(request.Supplier.Email)
                ? null
                : request.Supplier.Email;
        }

        var updated = await _supplierRepository.UpdateAsync(supplier);
        return _mapper.Map<SupplierDto>(updated);
    }
}


