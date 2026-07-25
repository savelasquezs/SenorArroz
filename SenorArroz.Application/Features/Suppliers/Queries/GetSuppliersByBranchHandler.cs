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
    private readonly IBranchContext _branchContext;

    public GetSuppliersByBranchHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper,
        IBranchContext branchContext)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
        _branchContext = branchContext;
    }

    public async Task<List<SupplierDto>> Handle(GetSuppliersByBranchQuery request, CancellationToken cancellationToken)
    {
        var branchId = _branchContext.RequireBranch(request.BranchId);

        var suppliers = await _supplierRepository.GetByBranchAsync(branchId, cancellationToken);
        return _mapper.Map<List<SupplierDto>>(suppliers);
    }
}


