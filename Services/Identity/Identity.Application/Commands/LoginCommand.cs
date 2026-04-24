using Identity.Application.DTOs;
using MediatR;

namespace Identity.Application.Commands;

public record LoginCommand(
    string Email,
    string Password
) : IRequest<AuthResponse>;