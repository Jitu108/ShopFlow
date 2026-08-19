using FluentAssertions;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Domain.Tests.Entities;

public class CategoryTests
{
    [Fact]
    public void Create_WithValidName_ShouldSetName()
    {
        var category = Category.Create("Electronics");

        category.Name.Should().Be("Electronics");
        category.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_WithBlankName_ShouldThrowDomainException()
    {
        var act = () => Category.Create(" ");

        act.Should().Throw<DomainException>();
    }
}
