using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using Identity.Domain.Exceptions;
using MediatR;

namespace Identity.Application.Commands;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponse>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        ITokenService tokenService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository         = userRepository;
        _tokenService           = tokenService;
    }

    public async Task<AuthResponse> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var existing = await _refreshTokenRepository.GetByTokenAsync(command.Token, ct)
            ?? throw new InvalidCredentialsException();

        if (existing.IsExpired)
            throw new InvalidCredentialsException();

        var user = await _userRepository.GetByIdAsync(existing.UserId, ct)
            ?? throw new InvalidCredentialsException();

        await _refreshTokenRepository.RevokeAsync(command.Token, ct);

        var jwt          = _tokenService.GenerateJwtToken(user);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, ct);

        return new AuthResponse(jwt, refreshToken.Token, user.Email, user.DisplayName, user.Role.ToString());
    }
}