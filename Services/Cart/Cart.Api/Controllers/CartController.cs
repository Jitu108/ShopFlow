using System.Security.Claims;
using Cart.Application.Commands;
using Cart.Application.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cart.Api.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly IMediator _mediator;

    public CartController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> GetCart(CancellationToken ct)
        => Ok(await _mediator.Send(new GetCartQuery(UserId), ct));

    [HttpPost("items")]
    public async Task<IActionResult> AddItem(AddCartItemRequest request, CancellationToken ct)
    {
        var command = new AddCartItemCommand(UserId, request.ProductId, request.ProductName, request.UnitPrice, request.Quantity);
        var result = await _mediator.Send(command, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPut("items/{productId:guid}")]
    public async Task<IActionResult> UpdateItem(Guid productId, UpdateCartItemRequest request, CancellationToken ct)
    {
        var command = new UpdateCartItemCommand(UserId, productId, request.Quantity);
        return Ok(await _mediator.Send(command, ct));
    }

    [HttpDelete("items/{productId:guid}")]
    public async Task<IActionResult> RemoveItem(Guid productId, CancellationToken ct)
    {
        await _mediator.Send(new RemoveCartItemCommand(UserId, productId), ct);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> ClearCart(CancellationToken ct)
    {
        await _mediator.Send(new ClearCartCommand(UserId), ct);
        return NoContent();
    }

    private Guid UserId => Guid.Parse(User.FindFirstValue("userId")!);
}

public record AddCartItemRequest(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);

public record UpdateCartItemRequest(int Quantity);
