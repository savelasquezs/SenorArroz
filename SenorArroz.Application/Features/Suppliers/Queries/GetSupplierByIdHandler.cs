using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSupplierByIdHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto?>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;

    public GetSupplierByIdHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
    }

    public async Task<SupplierDto?> Handle(GetSupplierByIdQuery request, CancellationToken cancellationToken)
    {
        var supplier = await _supplierRepository.GetByIdAsync(request.Id, cancellationToken);
        return supplier == null ? null : _mapper.Map<SupplierDto>(supplier);
    }
}
