using Cart.Application.DTOs;
using Cart.Application.Interfaces;
using Cart.Application.Queries;
using FluentAssertions;
using NSubstitute;

namespace Cart.Application.Tests.Queries;

public class GetCartQueryHandlerTests
{
    private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
    private readonly GetCartQueryHandler _handler;

    public GetCartQueryHandlerTests()
    {
        _handler = new GetCartQueryHandler(_cartRepository);
    }

    [Fact]
    public async Task Handle_WithEmptyCart_ShouldReturnEmptyList()
    {
        var userId = Guid.NewGuid();
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto>());

        var result = await _handler.Handle(new GetCartQuery(userId), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithItemsInCart_ShouldReturnAllItems()
    {
        var userId = Guid.NewGuid();
        var item = new CartItemDto(Guid.NewGuid(), "Widget", 9.99m, 2);
        _cartRepository.GetCartAsync(userId, default)
            .Returns(new Dictionary<Guid, CartItemDto> { [item.ProductId] = item });

        var result = await _handler.Handle(new GetCartQuery(userId), default);

        result.Should().ContainSingle().Which.Should().Be(item);
    }
}
