using MassTransit;
using Product.Application.Interfaces;
using ShopFlow.Shared.Events;

namespace Product.Infrastructure.Events;

public class CartStockAdjustedConsumer : IConsumer<CartStockAdjustedEvent>
{
    private readonly IProductRepository _productRepository;

    public CartStockAdjustedConsumer(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task Consume(ConsumeContext<CartStockAdjustedEvent> context)
    {
        var message = context.Message;
        if (message.QuantityDelta == 0)
        {
            return;
        }

        var product = await _productRepository.GetByIdAsync(message.ProductId, context.CancellationToken);
        if (product is null)
        {
            return;
        }

        if (message.QuantityDelta > 0)
        {
            product.DecrementStock(message.QuantityDelta);
        }
        else
        {
            product.IncrementStock(-message.QuantityDelta);
        }

        await _productRepository.UpdateAsync(product, context.CancellationToken);
    }
}
