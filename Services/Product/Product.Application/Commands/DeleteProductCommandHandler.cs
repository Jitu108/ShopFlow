using MediatR;
using Product.Application.Interfaces;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Commands;

public class DeleteProductCommandHandler : IRequestHandler<DeleteProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICacheService _cacheService;

    public DeleteProductCommandHandler(IProductRepository productRepository, ICacheService cacheService)
    {
        _productRepository = productRepository;
        _cacheService = cacheService;
    }

    public async Task Handle(DeleteProductCommand command, CancellationToken ct)
    {
        var product = await _productRepository.GetByIdAsync(command.Id, ct)
            ?? throw new NotFoundException(nameof(ProductEntity), command.Id);

        if (product.VendorId != command.VendorId)
            throw new NotFoundException(nameof(ProductEntity), command.Id);

        product.Deactivate();

        await _productRepository.UpdateAsync(product, ct);
        await _cacheService.RemoveAsync(CacheKeys.Product(product.Id), ct);
        await _cacheService.RemoveAsync(CacheKeys.Catalog, ct);
    }
}
