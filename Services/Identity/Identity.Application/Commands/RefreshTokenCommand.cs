using Identity.Application.DTOs;
using MediatR;

namespace Identity.Application.Commands;

public record RefreshTokenCommand(string Token) : IRequest<AuthResponse>;