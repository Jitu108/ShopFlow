using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;

namespace Product.Application.Queries;

public class GetVendorProductsQueryHandler : IRequestHandler<GetVendorProductsQuery, IReadOnlyList<ProductDto>>
{
    private readonly IProductRepository _productRepository;

    public GetVendorProductsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<IReadOnlyList<ProductDto>> Handle(GetVendorProductsQuery query, CancellationToken ct)
    {
        var products = await _productRepository.GetByVendorIdAsync(query.VendorId, ct);

        return products.Select(p => p.ToDto()).ToList();
    }
}
