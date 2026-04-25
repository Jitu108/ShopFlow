using Identity.Application.DTOs;
using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using MediatR;

namespace Identity.Application.Commands;

public class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, AuthResponse>
{
    private readonly IUserRepository _userRepository;
    private readonly ITokenService _tokenService;

    public RegisterUserCommandHandler(IUserRepository userRepository, ITokenService tokenService)
    {
        _userRepository = userRepository;
        _tokenService   = tokenService;
    }

    public async Task<AuthResponse> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        if (await _userRepository.ExistsByEmailAsync(command.Email, ct))
            throw new DuplicateEmailException(command.Email);

        var user = ApplicationUser.Create(command.Email, command.DisplayName);

        var createdUser   = await _userRepository.CreateAsync(user, command.Password, ct);
        var jwt           = _tokenService.GenerateJwtToken(createdUser);
        var refreshToken  = await _tokenService.GenerateRefreshTokenAsync(createdUser.Id, ct);

        return new AuthResponse(jwt, refreshToken.Token, createdUser.Email, createdUser.DisplayName, createdUser.Role.ToString());
    }
}