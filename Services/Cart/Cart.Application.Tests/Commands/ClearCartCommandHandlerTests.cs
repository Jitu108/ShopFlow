using Cart.Application.Commands;
using Cart.Application.Interfaces;
using NSubstitute;

namespace Cart.Application.Tests.Commands;

public class ClearCartCommandHandlerTests
{
    private readonly ICartRepository _cartRepository = Substitute.For<ICartRepository>();
    private readonly ClearCartCommandHandler _handler;

    public ClearCartCommandHandlerTests()
    {
        _handler = new ClearCartCommandHandler(_cartRepository);
    }

    [Fact]
    public async Task Handle_ShouldCall_ClearCartAsync_Once()
    {
        var userId = Guid.NewGuid();
        var command = new ClearCartCommand(userId);

        await _handler.Handle(command, default);

        await _cartRepository.Received(1).ClearCartAsync(userId, default);
    }
}
