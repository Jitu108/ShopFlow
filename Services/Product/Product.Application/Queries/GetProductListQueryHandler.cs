using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;

namespace Product.Application.Queries;

public class GetProductListQueryHandler : IRequestHandler<GetProductListQuery, IReadOnlyList<ProductDto>>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public GetProductListQueryHandler(IProductRepository productRepository, ICacheService cacheService)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
    }

    public async Task<IReadOnlyList<ProductDto>> Handle(GetProductListQuery query, CancellationToken ct)
    {
        var cached = await _cacheService.GetAsync<IReadOnlyList<ProductDto>>(CacheKeys.Catalog, ct);
        if (cached is not null)
            return cached;

        var products = await _productRepository.GetAllActiveAsync(ct);
        var dtos = products.Select(p => p.ToDto()).ToList();

        await _cacheService.SetAsync<IReadOnlyList<ProductDto>>(CacheKeys.Catalog, dtos, CacheDuration, ct);

        return dtos;
    }
}
