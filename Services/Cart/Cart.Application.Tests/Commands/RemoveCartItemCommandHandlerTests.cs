using Cart.Application.Commands;
using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using NSubstitute;

namespace Cart.Application.Tests.Commands;

public class RemoveCartItemCommandHandlerTests
{
    private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
    private readonly ICartEventPublisher _cartEventPublisher = Substitute.For<ICartEventPublisher>();
    private readonly RemoveCartItemCommandHandler _handler;

    public RemoveCartItemCommandHandlerTests()
    {
        _handler = new RemoveCartItemCommandHandler(_cartRepository, _cartEventPublisher);
    }

    [Fact]
    public async Task Handle_ShouldCall_RemoveItemAsync_Once()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var command = new RemoveCartItemCommand(userId, productId);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto>());

        await _handler.Handle(command, default);

        await _cartRepository.Received(1).RemoveItemAsync(userId, productId, default);
    }

    [Fact]
    public async Task Handle_WhenItemExists_ShouldPublishStockAdjusted_WithNegativeOfExistingQuantity()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var command = new RemoveCartItemCommand(userId, productId);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = new CartItemDto(productId, "Widget", 9.99m, 4) });

        await _handler.Handle(command, default);

        await _cartEventPublisher.Received(1).PublishStockAdjustedAsync(productId, -4, default);
    }

    [Fact]
    public async Task Handle_WhenItemDoesNotExist_ShouldNotPublishStockAdjusted()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var command = new RemoveCartItemCommand(userId, productId);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto>());

        await _handler.Handle(command, default);

        await _cartEventPublisher.DidNotReceive().PublishStockAdjustedAsync(
            Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }
}
