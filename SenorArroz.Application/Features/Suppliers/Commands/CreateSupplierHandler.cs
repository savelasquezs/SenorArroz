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
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public CreateSupplierHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<SupplierDto> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        if (!Roles.IsSuperadminOrAdminOrCashier(_currentUser.Role))
        {
            throw new BusinessException("No tienes permisos para crear proveedores.");
        }

        var supplier = new Supplier
        {
            BranchId = null,
            Name = request.Supplier.Name.Trim(),
            Phone = request.Supplier.Phone.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Supplier.Address) ? null : request.Supplier.Address.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Supplier.Email) ? null : request.Supplier.Email.Trim()
        };

        var created = await _supplierRepository.CreateAsync(supplier, cancellationToken);
        return _mapper.Map<SupplierDto>(created);
    }
}
