using Cart.Application.Commands;
using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using Cart.Domain.Exceptions;
using FluentAssertions;
using NSubstitute;

namespace Cart.Application.Tests.Commands;

public class UpdateCartItemCommandHandlerTests
{
    private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
    private readonly ICartEventPublisher _cartEventPublisher = Substitute.For<ICartEventPublisher>();
    private readonly UpdateCartItemCommandHandler _handler;

    public UpdateCartItemCommandHandlerTests()
    {
        _handler = new UpdateCartItemCommandHandler(_cartRepository, _cartEventPublisher);
    }

    [Fact]
    public async Task Handle_WithExistingProduct_ShouldUpdateQuantity()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new CartItemDto(productId, "Widget", 9.99m, 1);
        var command = new UpdateCartItemCommand(userId, productId, 5);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = existing });

        var result = await _handler.Handle(command, default);

        result.Quantity.Should().Be(5);
        result.ProductName.Should().Be("Widget");
        await _cartRepository.Received(1).UpsertItemAsync(userId,
            Arg.Is<CartItemDto>(i => i.Quantity == 5), default);
    }

    [Fact]
    public async Task Handle_WithUnknownProduct_ShouldThrowNotFoundException()
    {
        var userId = Guid.NewGuid();
        var command = new UpdateCartItemCommand(userId, Guid.NewGuid(), 5);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto>());

        var act = () => _handler.Handle(command, default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenQuantityIncreases_ShouldPublishStockAdjusted_WithPositiveDelta()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new CartItemDto(productId, "Widget", 9.99m, 1);
        var command = new UpdateCartItemCommand(userId, productId, 5);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = existing });

        await _handler.Handle(command, default);

        await _cartEventPublisher.Received(1).PublishStockAdjustedAsync(productId, 4, default);
    }

    [Fact]
    public async Task Handle_WhenQuantityDecreases_ShouldPublishStockAdjusted_WithNegativeDelta()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new CartItemDto(productId, "Widget", 9.99m, 5);
        var command = new UpdateCartItemCommand(userId, productId, 2);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = existing });

        await _handler.Handle(command, default);

        await _cartEventPublisher.Received(1).PublishStockAdjustedAsync(productId, -3, default);
    }

    [Fact]
    public async Task Handle_WhenQuantityUnchanged_ShouldNotPublishStockAdjusted()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new CartItemDto(productId, "Widget", 9.99m, 3);
        var command = new UpdateCartItemCommand(userId, productId, 3);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = existing });

        await _handler.Handle(command, default);

        await _cartEventPublisher.DidNotReceive().PublishStockAdjustedAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
