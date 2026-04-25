using Identity.Application.Commands;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterUserCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken ct)
        => Ok(await _mediator.Send(command, ct));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
        => Ok(await _mediator.Send(new RefreshTokenCommand(request.Token), ct));

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await _mediator.Send(new LogoutCommand(request.Token), ct);
        return NoContent();
    }
}

public record RefreshRequest(string Token);