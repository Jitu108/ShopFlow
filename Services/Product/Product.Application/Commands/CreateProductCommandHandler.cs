using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;
using Product.Domain.Entities;

namespace Product.Application.Commands;

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public CreateProductCommandHandler(IProductRepository productRepository, ICacheService cacheService)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
    }

    public async Task<ProductDto> Handle(CreateProductCommand command, CancellationToken ct)
    {
        var product = ProductEntity.Create(
            command.VendorId, command.Name, command.Description, command.Price, command.StockQuantity, command.CategoryId);

        await _productRepository.AddAsync(product, ct);
        await _cacheService.RemoveAsync(CacheKeys.Catalog, ct);

        return product.ToDto();
    }
}
