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

public class CheckStockConsumerTests
{
    [Fact]
    public async Task Consume_WhenAllItemsHaveSufficientStock_ShouldRespondAvailable()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 10, Guid.NewGuid());
        var productRepository = Substitute.For<IProductRepository>();
        productRepository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CheckStockConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var client = harness.GetRequestClient<CheckStockRequest>();
        var response = await client.GetResponse<CheckStockResponse>(
            new CheckStockRequest([new OrderItemDto(product.Id, "Widget", 9.99m, 5)]));

        response.Message.IsAvailable.Should().BeTrue();
        response.Message.InsufficientProductIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Consume_WhenQuantityExceedsStock_ShouldRespondUnavailable_WithProductId()
    {
        var product = ProductEntity.Create(Guid.NewGuid(), "Widget", "desc", 9.99m, 2, Guid.NewGuid());
        var productRepository = Substitute.For<IProductRepository>();
        productRepository.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CheckStockConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var client = harness.GetRequestClient<CheckStockRequest>();
        var response = await client.GetResponse<CheckStockResponse>(
            new CheckStockRequest([new OrderItemDto(product.Id, "Widget", 9.99m, 5)]));

        response.Message.IsAvailable.Should().BeFalse();
        response.Message.InsufficientProductIds.Should().ContainSingle().Which.Should().Be(product.Id);
    }

    [Fact]
    public async Task Consume_WhenProductNotFound_ShouldRespondUnavailable()
    {
        var productId = Guid.NewGuid();
        var productRepository = Substitute.For<IProductRepository>();
        productRepository.GetByIdAsync(productId, Arg.Any<CancellationToken>()).Returns((ProductEntity?)null);

        await using var provider = new ServiceCollection()
            .AddSingleton(productRepository)
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<CheckStockConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var client = harness.GetRequestClient<CheckStockRequest>();
        var response = await client.GetResponse<CheckStockResponse>(
            new CheckStockRequest([new OrderItemDto(productId, "Widget", 9.99m, 1)]));

        response.Message.IsAvailable.Should().BeFalse();
        response.Message.InsufficientProductIds.Should().Contain(productId);
    }
}
