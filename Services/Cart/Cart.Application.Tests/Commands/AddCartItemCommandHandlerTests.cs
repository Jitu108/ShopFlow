using Cart.Application.Commands;
using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace Cart.Application.Tests.Commands;

public class AddCartItemCommandHandlerTests
{
    private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
    private readonly ICartEventPublisher _cartEventPublisher = Substitute.For<ICartEventPublisher>();
    private readonly AddCartItemCommandHandler _handler;

    public AddCartItemCommandHandlerTests()
    {
        _handler = new AddCartItemCommandHandler(_cartRepository, _cartEventPublisher);
    }

    [Fact]
    public async Task Handle_WithNewProduct_ShouldUpsertWithRequestedQuantity()
    {
        var userId = Guid.NewGuid();
        var command = new AddCartItemCommand(userId, Guid.NewGuid(), "Widget", 9.99m, 2);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto>());

        var result = await _handler.Handle(command, default);

        result.Quantity.Should().Be(2);
        await _cartRepository.Received(1).UpsertItemAsync(userId,
            Arg.Is<CartItemDto>(i => i.ProductId == command.ProductId && i.Quantity == 2), default);
    }

    [Fact]
    public async Task Handle_WithExistingProduct_ShouldAddToExistingQuantity()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new CartItemDto(productId, "Widget", 9.99m, 3);
        var command = new AddCartItemCommand(userId, productId, "Widget", 9.99m, 2);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = existing });

        var result = await _handler.Handle(command, default);

        result.Quantity.Should().Be(5);
    }

    [Fact]
    public async Task Handle_ShouldPublishStockAdjusted_WithRequestedQuantityAsDelta()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var existing = new CartItemDto(productId, "Widget", 9.99m, 3);
        var command = new AddCartItemCommand(userId, productId, "Widget", 9.99m, 2);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [productId] = existing });

        await _handler.Handle(command, default);

        await _cartEventPublisher.Received(1).PublishStockAdjustedAsync(productId, 2, default);
    }
}
