using Cart.Domain.Exceptions;
using FluentAssertions;

namespace Cart.Domain.Tests.Exceptions;

public class NotFoundExceptionTests
{
    [Fact]
    public void Constructor_ShouldFormatMessage_WithEntityNameAndKey()
    {
        var key = Guid.NewGuid();

        var exception = new NotFoundException("CartItem", key);

        exception.Message.Should().Be($"CartItem with key '{key}' was not found.");
    }
}
