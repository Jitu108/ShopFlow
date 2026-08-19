using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Queries;

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public GetProductByIdQueryHandler(IProductRepository productRepository, ICacheService cacheService)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
    }

    public async Task<ProductDto> Handle(GetProductByIdQuery query, CancellationToken ct)
    {
        var cacheKey = CacheKeys.Product(query.Id);

        var cached = await _cacheService.GetAsync<ProductDto>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var product = await _productRepository.GetByIdAsync(query.Id, ct)
            ?? throw new NotFoundException(nameof(ProductEntity), query.Id);

        var dto = product.ToDto();
        await _cacheService.SetAsync(cacheKey, dto, CacheDuration, ct);

        return dto;
    }
}
