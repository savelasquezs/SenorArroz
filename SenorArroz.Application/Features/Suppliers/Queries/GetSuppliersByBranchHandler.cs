using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSuppliersByBranchHandler : IRequestHandler<GetSuppliersByBranchQuery, List<SupplierDto>>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;

    public GetSuppliersByBranchHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
    }

    public async Task<List<SupplierDto>> Handle(GetSuppliersByBranchQuery request, CancellationToken cancellationToken)
    {
        var suppliers = await _supplierRepository.GetAllAsync(cancellationToken);
        return _mapper.Map<List<SupplierDto>>(suppliers);
    }
}
