using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Suppliers.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;
using SenorArroz.Shared.Models;

namespace SenorArroz.Application.Features.Suppliers.Queries;

public class GetSuppliersHandler : IRequestHandler<GetSuppliersQuery, PagedResult<SupplierDto>>
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetSuppliersHandler(
        ISupplierRepository supplierRepository,
        IMapper mapper,
        ICurrentUser currentUser)
    {
        _supplierRepository = supplierRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        int? branchFilter = null;

        if (_currentUser.Role != "superadmin")
        {
            branchFilter = _currentUser.BranchId;
        }
        else if (request.BranchId.HasValue && request.BranchId > 0)
        {
            branchFilter = request.BranchId;
        }

        var pagedSuppliers = await _supplierRepository.GetPagedAsync(
            branchFilter,
            request.Search,
            request.Page,
            request.PageSize,
            request.SortBy,
            request.SortOrder);

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


