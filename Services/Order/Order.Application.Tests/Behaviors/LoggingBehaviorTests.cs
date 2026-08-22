using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Order.Application.Behaviors;

namespace Order.Application.Tests.Behaviors;

public class LoggingBehaviorTests
{
    public record SampleRequest(string Name) : MediatR.IRequest<string>;

    [Fact]
    public async Task Handle_ShouldCallNext_AndReturnItsResult()
    {
        var logger = Substitute.For<ILogger<LoggingBehavior<SampleRequest, string>>>();
        var behavior = new LoggingBehavior<SampleRequest, string>(logger);

        var result = await behavior.Handle(new SampleRequest("ok"), _ => Task.FromResult("handled"), default);

        result.Should().Be("handled");
    }
}
