using Identity.Application.Interfaces;
using Identity.Domain.Entities;
using Identity.Domain.Enums;
using Identity.Domain.Exceptions;
using MediatR;

namespace Identity.Application.Commands;

public record AssignRoleCommand(Guid UserId, string Role) : IRequest;

public class AssignRoleCommandHandler : IRequestHandler<AssignRoleCommand>
{
    private readonly IUserRepository _userRepository;

    public AssignRoleCommandHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task Handle(AssignRoleCommand command, CancellationToken ct)
    {
        var user = await _userRepository.GetByIdAsync(command.UserId, ct)
            ?? throw new NotFoundException(nameof(ApplicationUser), command.UserId);

        if (!Enum.TryParse<UserRole>(command.Role, ignoreCase: true, out var role))
            throw new DomainException($"Invalid role '{command.Role}'.");

        user.AssignRole(role);
        await _userRepository.UpdateAsync(user, ct);
    }
}