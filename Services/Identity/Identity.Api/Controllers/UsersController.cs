using System.Security.Claims;
using Identity.Application.Commands;
using Identity.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

[ApiController]
[Route("api")]
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public UsersController(IMediator mediator) => _mediator = mediator;

    [HttpGet("users/me")]
    [Authorize]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue("userId")!);
        return Ok(await _mediator.Send(new GetCurrentUserQuery(userId), ct));
    }

    [HttpPost("admin/users/{id}/assign-role")]
    [Authorize(Policy = "RequireAdmin")]
    public async Task<IActionResult> AssignRole(Guid id, AssignRoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(new AssignRoleCommand(id, request.Role), ct);
        return Ok();
    }
}

public record AssignRoleRequest(string Role);