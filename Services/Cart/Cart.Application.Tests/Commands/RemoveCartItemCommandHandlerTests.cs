using Cart.Application.Commands;
using Cart.Application.Interfaces;
using NSubstitute;

namespace Cart.Application.Tests.Commands;

public class RemoveCartItemCommandHandlerTests
{
    private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
    private readonly RemoveCartItemCommandHandler _handler;

    public RemoveCartItemCommandHandlerTests()
    {
        _handler = new RemoveCartItemCommandHandler(_cartRepository);
    }

    [Fact]
    public async Task Handle_ShouldCall_RemoveItemAsync_Once()
    {
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var command = new RemoveCartItemCommand(userId, productId);

        await _handler.Handle(command, default);

        await _cartRepository.Received(1).RemoveItemAsync(userId, productId, default);
    }
}
