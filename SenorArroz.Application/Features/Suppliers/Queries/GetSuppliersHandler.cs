using AutoMapper;
using MediatR;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSuppliersHandler : IRequestHandler<GetSuppliersQuery, PagedResult<SupplierDto>>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;

    public GetSuppliersHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
    }

    public async Task<PagedResult<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        var pagedSuppliers = await _supplierRepository.GetPagedAsync(
            request.Search,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder,
            cancellationToken);

        return new PagedResult<SupplierDto>
        {
            Items = _mapper.Map<List<SupplierDto>>(pagedSuppliers.Items),
            TotalCount = pagedSuppliers.TotalCount,
            Page = pagedSuppliers.Page,
            PageSize = pagedSuppliers.PageSize,
            TotalPages = pagedSuppliers.TotalPages
        };
    }
}
