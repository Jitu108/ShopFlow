using MediatR;

namespace Identity.Application.Commands;

public record VerifyEmailCommand(Guid UserId) : IRequest;
