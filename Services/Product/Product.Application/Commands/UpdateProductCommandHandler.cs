using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Commands;

public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, ProductDto>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public UpdateProductCommandHandler(IProductRepository productRepository, ICacheService cacheService)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
    }

    public async Task<ProductDto> Handle(UpdateProductCommand command, CancellationToken ct)
    {
        var product = await _productRepository.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException(nameof(ProductEntity), command.Id);

        if (product.VendorId != command.VendorId)
            throw new NotFoundException(nameof(ProductEntity), command.Id);

        product.Update(command.Name, command.Description, command.Price, command.StockQuantity, command.CategoryId);

        await _productRepository.UpdateAsync(product, ct);
        await _cacheService.RemoveAsync(CacheKeys.Product(product.Id), ct);
        await _cacheService.RemoveAsync(CacheKeys.Catalog, ct);

        return product.ToDto();
    }
}
