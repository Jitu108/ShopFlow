using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;

namespace Product.Application.Queries;

public class GetCategoryListQueryHandler : IRequestHandler<GetCategoryListQuery, IReadOnlyList<CategoryDto>>
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoryListQueryHandler(ICategoryRepository categoryRepository)
        => _categoryRepository = categoryRepository;

    public async Task<IReadOnlyList<CategoryDto>> Handle(GetCategoryListQuery query, CancellationToken ct)
    {
        var categories = await _categoryRepository.GetAllAsync(ct);
        return categories.Select(c => c.ToDto()).ToList();
    }
}
