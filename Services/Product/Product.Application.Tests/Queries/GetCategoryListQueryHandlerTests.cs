using FluentAssertions;
using NSubstitute;
using Product.Application.Interfaces;
using Product.Application.Queries;
using Product.Domain.Entities;

namespace Product.Application.Tests.Queries;

public class GetCategoryListQueryHandlerTests
{
    private readonly ICategoryRepository _categoryRepository = Substitute.For<ICategoryRepository>();
    private readonly GetCategoryListQueryHandler _handler;

    public GetCategoryListQueryHandlerTests()
    {
        _handler = new GetCategoryListQueryHandler(_categoryRepository);
    }

    [Fact]
    public async Task Handle_ShouldReturn_AllCategoriesAsDtos()
    {
        var category = Category.Create("Electronics");
        _categoryRepository.GetAllAsync(default).Returns(new List<Category> { category });

        var result = await _handler.Handle(new GetCategoryListQuery(), default);

        result.Should().ContainSingle(c => c.Id == category.Id && c.Name == "Electronics");
    }

    [Fact]
    public async Task Handle_WithNoCategories_ShouldReturnEmptyList()
    {
        _categoryRepository.GetAllAsync(default).Returns(new List<Category>());

        var result = await _handler.Handle(new GetCategoryListQuery(), default);

        result.Should().BeEmpty();
    }
}
