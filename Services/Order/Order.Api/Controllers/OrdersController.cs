using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Order.Application.Commands;
using Order.Application.DTOs;
using Order.Application.Queries;

namespace Order.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [Authorize(Policy = "RequireVerifiedEmail")]
    public async Task<IActionResult> PlaceOrder(PlaceOrderRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new PlaceOrderCommand(CustomerId, CustomerEmail, request.Items), ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetMyOrders(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMyOrdersQuery(CustomerId), ct));

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetOrderByIdQuery(id, CustomerId, IsAdmin), ct));

    [HttpPut("{id:guid}/confirm")]
    [Authorize]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new ConfirmOrderCommand(id, CustomerId), ct));

    private Guid CustomerId => Guid.Parse(User.FindFirstValue("userId")!);
    private string CustomerEmail => User.FindFirstValue(ClaimTypes.Email)!;
    private bool IsAdmin => User.IsInRole("Admin");
}

public record PlaceOrderRequest(List<OrderItemRequestDto> Items);
