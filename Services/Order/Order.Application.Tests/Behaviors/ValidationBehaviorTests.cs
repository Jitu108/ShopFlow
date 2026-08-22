using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using NSubstitute;
using Order.Application.Behaviors;

namespace Order.Application.Tests.Behaviors;

public class ValidationBehaviorTests
{
    public record SampleRequest(string Name) : MediatR.IRequest<string>;

    [Fact]
    public async Task Handle_WithValidRequest_ShouldCallNext()
    {
        var validator = Substitute.For<IValidator<SampleRequest>>();
        validator.Validate(Arg.Any<ValidationContext<SampleRequest>>()).Returns(new ValidationResult());

        var behavior = new ValidationBehavior<SampleRequest, string>(new[] { validator });
        var nextCalled = false;

        var result = await behavior.Handle(new SampleRequest("ok"), _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, default);

        nextCalled.Should().BeTrue();
        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ShouldThrowValidationException_AndNotCallNext()
    {
        var validator = Substitute.For<IValidator<SampleRequest>>();
        var failures = new List<ValidationFailure> { new("Name", "Name is required") };
        validator.Validate(Arg.Any<ValidationContext<SampleRequest>>()).Returns(new ValidationResult(failures));

        var behavior = new ValidationBehavior<SampleRequest, string>(new[] { validator });
        var nextCalled = false;

        var act = () => behavior.Handle(new SampleRequest(""), _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, default);

        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }
}
