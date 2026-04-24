using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using Identity.Domain.Exceptions;
using MediatR;

namespace Identity.Application.Commands;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;

    public LoginCommandHandler(IUserRepository userRepository, ITokenService tokenService)
    {
        _userRepository = userRepository;
        _tokenService   = tokenService;
    }

    public async Task<AuthResponse> Handle(LoginCommand command, CancellationToken ct)
    {
        var user = await _userRepository.FindByEmailAsync(command.Email, ct)
            ?? throw new InvalidCredentialsException();

        if (!await _userRepository.CheckPasswordAsync(user, command.Password, ct))
            throw new InvalidCredentialsException();

        var jwt          = _tokenService.GenerateJwtToken(user);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, ct);

        return new AuthResponse(jwt, refreshToken.Token, user.Email, user.DisplayName, user.Role.ToString());
    }
}