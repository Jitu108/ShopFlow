using MediatR;
using Product.Application.DTOs;
using Product.Application.Interfaces;
using Product.Application.Mapping;
using Product.Domain.Entities;
using Product.Domain.Exceptions;

namespace Product.Application.Commands;

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, CategoryDto>
{
    private readonly ICategoryRepository _categoryRepository;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository)
        => _categoryRepository = categoryRepository;

    public async Task<CategoryDto> Handle(CreateCategoryCommand command, CancellationToken ct)
    {
        if (await _categoryRepository.ExistsByNameAsync(command.Name, ct))
            throw new DomainException($"Category '{command.Name}' already exists.");

        var category = Category.Create(command.Name);
        await _categoryRepository.AddAsync(category, ct);

        return category.ToDto();
    }
}
