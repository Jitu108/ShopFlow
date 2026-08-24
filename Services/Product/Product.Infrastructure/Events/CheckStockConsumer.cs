using MassTransit;
using Product.Application.Interfaces;
using ShopFlow.Shared.Events;

namespace Product.Infrastructure.Events;

public class CheckStockConsumer : IConsumer<CheckStockRequest>
{
    private readonly IProductRepository _productRepository;

    public CheckStockConsumer(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task Consume(ConsumeContext<CheckStockRequest> context)
    {
        var insufficientProductIds = new List<Guid>();

        foreach (var item in context.Message.Items)
        {
            var product = await _productRepository.GetByIdAsync(item.ProductId, context.CancellationToken);
            if (product is null || product.StockQuantity < item.Quantity)
            {
                insufficientProductIds.Add(item.ProductId);
            }
        }

        await context.RespondAsync(new CheckStockResponse(insufficientProductIds.Count == 0, insufficientProductIds));
    }
}
