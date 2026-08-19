using MediatR;
using Product.Application.DTOs;

namespace Product.Application.Commands;

public record CreateCategoryCommand(string Name) : IRequest<CategoryDto>;
