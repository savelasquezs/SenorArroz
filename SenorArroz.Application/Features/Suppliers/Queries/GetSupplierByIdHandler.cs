using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSupplierByIdHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto?>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetSupplierByIdHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<SupplierDto?> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
    {
        var supplier = await _supplierRepository.GetByIdAsync(request.Id);
        if (supplier == null)
        {
            return null;
        }

        if (_currentUser.Role != "superadmin" && supplier.BranchId != _currentUser.BranchId)
        {
            return null;
        }

        return _mapper.Map<SupplierDto>(supplier);
    }
}


