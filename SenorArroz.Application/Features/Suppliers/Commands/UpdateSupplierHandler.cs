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
        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
        {
            throw new BusinessException("No tienes permisos para actualizar proveedores.");
        }

        var supplier = await _supplierRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");

        if (!string.IsNullOrWhiteSpace(request.Supplier.Name) &&
            !string.Equals(request.Supplier.Name.Trim(), supplier.Name, StringComparison.OrdinalIgnoreCase))
        {
            supplier.Name = request.Supplier.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Supplier.Phone) &&
            !string.Equals(request.Supplier.Phone.Trim(), supplier.Phone, StringComparison.OrdinalIgnoreCase))
        {
            supplier.Phone = request.Supplier.Phone.Trim();
        }

        if (request.Supplier.Address is not null)
        {
            supplier.Address = string.IsNullOrWhiteSpace(request.Supplier.Address)
                ? null
                : request.Supplier.Address.Trim();
        }

        if (request.Supplier.Email is not null)
        {
            supplier.Email = string.IsNullOrWhiteSpace(request.Supplier.Email)
                ? null
                : request.Supplier.Email.Trim();
        }

        var updated = await _supplierRepository.UpdateAsync(supplier, cancellationToken);
        return _mapper.Map<SupplierDto>(updated);
    }
}
