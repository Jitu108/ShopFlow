using FluentAssertions;
using NSubstitute;
using Product.Application.Commands;
using Product.Application.Interfaces;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Tests.Commands;

public class CreateCategoryCommandHandlerTests
{
    private readonly ICategoryRepository _categoryRepository = Substitute.For<ICategoryRepository>();
    private readonly CreateCategoryCommandHandler _handler;

    public CreateCategoryCommandHandlerTests()
    {
        _handler = new CreateCategoryCommandHandler(_categoryRepository);
    }

    [Fact]
    public async Task Handle_WithValidCommand_ShouldReturnCategoryDto()
    {
        var command = new CreateCategoryCommand("Electronics");

        var result = await _handler.Handle(command, default);

        result.Name.Should().Be("Electronics");
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ShouldCall_AddAsync_Once()
    {
        var command = new CreateCategoryCommand("Electronics");

        await _handler.Handle(command, default);

        await _categoryRepository.Received(1).AddAsync(Arg.Any<Category>(), default);
    }

    [Fact]
    public async Task Handle_WithDuplicateName_ShouldThrowDomainException()
    {
        var command = new CreateCategoryCommand("Electronics");
        _categoryRepository.ExistsByNameAsync(command.Name, default).Returns(true);

        var act = async () => await _handler.Handle(command, default);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*already exists*");
    }
}
