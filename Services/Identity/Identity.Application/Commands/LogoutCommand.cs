using Identity.Application.Interfaces;
using MediatR;

namespace Identity.Application.Commands;

public record LogoutCommand(string Token) : IRequest;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokenRepository)
        => _refreshTokenRepository = refreshTokenRepository;

    public async Task Handle(LogoutCommand command, CancellationToken ct)
        => await _refreshTokenRepository.RevokeAsync(command.Token, ct);
}