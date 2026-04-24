using Identity.Application.DTOs;
using MediatR;

namespace Identity.Application.Commands;

public record RegisterUserCommand(
    string Email,
    string Password,
    string DisplayName
) : IRequest<AuthResponse>;