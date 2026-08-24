using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Product.Application.Interfaces;
using Product.Domain.Entities;
using Product.Infrastructure.Events;
using ShopFlow.Shared.Events;

namespace Product.Infrastructure.Tests.Events;

public class CartStockAdjustedConsumerTests
{
    [Fact]
    public async Task Consume_WithPositiveDelta_ShouldDecrementStock()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var productRepository = Substitute.For<IProductRepository>();
        productRepository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CartStockAdjustedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new CartStockAdjustedEvent(product.Id, 3));

        (await harness.Consumed.Any<CartStockAdjustedEvent>()).Should().BeTrue();
        product.StockQuantity.Should().Be(7);
        await productRepository.Received(1).UpdateAsync(product, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WithNegativeDelta_ShouldIncrementStock()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var productRepository = Substitute.For<IProductRepository>();
        productRepository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CartStockAdjustedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new CartStockAdjustedEvent(product.Id, -4));

        (await harness.Consumed.Any<CartStockAdjustedEvent>()).Should().BeTrue();
        product.StockQuantity.Should().Be(14);
        await productRepository.Received(1).UpdateAsync(product, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WithZeroDelta_ShouldNotTouchRepository()
    {
        var productRepository = Substitute.For<IProductRepository>();

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CartStockAdjustedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new CartStockAdjustedEvent(Guid.NewGuid(), 0));

        (await harness.Consumed.Any<CartStockAdjustedEvent>()).Should().BeTrue();
        await productRepository.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_WhenProductNotFound_ShouldSkipWithoutThrowing()
    {
        var productRepository = Substitute.For<IProductRepository>();
        productRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ProductEntity?)null);

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CartStockAdjustedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new CartStockAdjustedEvent(Guid.NewGuid(), 2));

        (await harness.Consumed.Any<CartStockAdjustedEvent>()).Should().BeTrue();
        await productRepository.DidNotReceive().UpdateAsync(Arg.Any<ProductEntity>(), Arg.Any<CancellationToken>());
    }
}
