// SenorArroz.Application/Features/Products/Queries/GetProductByIdHandler.cs
using AutoMapper;
using MediatR;
using SenorArroz.Application.Common.Interfaces;
using SenorArroz.Application.Features.Products.DTOs;
using SenorArroz.Domain.Interfaces.Repositories;

namespace SenorArroz.Application.Features.Products.Queries;

public class GetProductByIdHandler : IRequestHandler<GetProductByIdQuery, ProductDto?>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;
    private readonly ICurrentUser _currentUser;

    public GetProductByIdHandler(IProductRepository productRepository, IMapper mapper, ICurrentUser currentUser)
    {
        _productRepository = productRepository;
        _mapper = mapper;
        _currentUser = currentUser;
    }

    public async Task<ProductDto?> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(request.Id);
        
        if (product == null)
            return null;

        // Check if user has access to this product's branch
        if (_currentUser.Role != "superadmin" && product.Category.BranchId != _currentUser.BranchId)
            return null;

        return _mapper.Map<ProductDto>(product);
    }
}
