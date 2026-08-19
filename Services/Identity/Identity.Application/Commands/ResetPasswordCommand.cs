using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Exceptions;
using MediatR;

namespace Identity.Application.Commands;

public record ResetPasswordCommand(Guid UserId, string NewPassword) : IRequest;

public class ResetPasswordCommandHandler : IRequestHandler<ResetPasswordCommand>
{
    private readonly IUserRepository _userRepository;

    public ResetPasswordCommandHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException(nameof(ApplicationUser), command.UserId);

        await _userRepository.ResetPasswordAsync(user, command.NewPassword, ct);
    }
}
