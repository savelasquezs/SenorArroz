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
        if (!Roles.IsAdminOrSuperadmin(_currentUser.Role))
        {
            throw new BusinessException("No tienes permisos para eliminar proveedores.");
        }

        _ = await _supplierRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Proveedor no encontrado.");

        return await _supplierRepository.DeleteAsync(request.Id, cancellationToken);
    }
}
