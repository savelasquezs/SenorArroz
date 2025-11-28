using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Domain.Exceptions;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Commands;

public class DeleteSupplierHandler : IRequestHandler<DeleteSupplierCommand, bool>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly ICurrentUser _currentUser;

    public DeleteSupplierHandler(
        ISupplierRepository supplierRepository,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _currentUser = currentUser;
    }

    public async Task<bool> Handle(DeleteSupplierCommand request, CancellationToken cancellationToken)
    {
        // Solo Admin y Superadmin pueden eliminar proveedores
        if (_currentUser.Role is not ("admin" or "superadmin"))
        {
            throw new BusinessException("No tienes permisos para eliminar proveedores.");
        }

        var supplier = await _supplierRepository.GetByIdAsync(request.Id)
            ?? throw new NotFoundException("Proveedor no encontrado.");

        if (_currentUser.Role != "superadmin" && supplier.BranchId != _currentUser.BranchId)
        {
            throw new BusinessException("No puedes eliminar proveedores de otra sucursal.");
        }

        return await _supplierRepository.DeleteAsync(request.Id);
    }
}


