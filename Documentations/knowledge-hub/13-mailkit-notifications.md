# MailKit &amp; Notification Email

## Abstract

Notification.Api's entire job is to react to `OrderPlacedEvent` and send the customer an order-confirmation email. It does this with **MailKit** (`MailKit` 4.17.0), a full-featured, standards-compliant SMTP/IMAP/POP3 client library for .NET, rather than the deprecated `System.Net.Mail.SmtpClient`. In development and tests, that email is never sent anywhere real — it's caught locally by **smtp4dev**, a fake SMTP server with a web UI and REST API, run as its own container in [docker-compose.yml](../../docker-compose.yml). This document covers what MailKit is, why ShopFlow chose it, the real send path from `OrderPlacedEvent` to an SMTP connection, the static email-template pattern, and how smtp4dev fits into both local development and the automated Infrastructure.Tests.

## What it is

**MailKit** is an open-source .NET library ([jstedfast/MailKit](https://github.com/jstedfast/MailKit)) providing full client implementations of SMTP, IMAP, and POP3, built on top of MimeKit for MIME message construction. ShopFlow only uses its SMTP client, `MailKit.Net.Smtp.SmtpClient`, plus MimeKit's `MimeMessage`/`MailboxAddress`/`TextPart` for building the message itself. Notification.Infrastructure's dependency on it is declared plainly in [Notification.Infrastructure.csproj](../../Services/Notification/Notification.Infrastructure/Notification.Infrastructure.csproj):

```xml
<PackageReference Include="MailKit" Version="4.17.0" />
```

**smtp4dev** (image `rnwood/smtp4dev:v3`) is a disposable, local SMTP server built for exactly this scenario: it accepts any SMTP connection and message, never relays anything onward, and exposes both a web UI and a JSON REST API (`/api/messages`, `/api/messages/{id}/plaintext`) to inspect what was "sent." It has no relationship to MailKit itself — it's just the receiving end MailKit's `SmtpClient` connects to in development and tests.

## Why ShopFlow uses it

1. **`System.Net.Mail.SmtpClient` is deprecated by Microsoft** and has been unmaintained for years — the .NET docs themselves recommend MailKit as the replacement for any new SMTP-sending code. Choosing MailKit from the start avoids building on an API surface Microsoft has explicitly told developers to migrate away from.
2. **Standards compliance and full control over the message.** MailKit's `MimeMessage`/`MimeKit` combination gives explicit control over `From`, `To`, `Subject`, and body parts (`TextPart("plain")`), and `SmtpClient` supports modern auth and TLS negotiation (`SecureSocketOptions.Auto`) that the old `SmtpClient` handled poorly or not at all.
3. **A real SMTP round trip is testable without a real mailbox.** Because MailKit just needs a host/port/credentials, smtp4dev can stand in for a real mail server both in the Docker Compose stack (so a developer never accidentally emails a real address while testing order flow) and in Testcontainers-based Infrastructure.Tests (so the send path is exercised against a real SMTP server, not a mock of `SmtpClient`).

## How it's used

### The send path — `MailKitEmailService`

[MailKitEmailService.cs](../../Services/Notification/Notification.Infrastructure/Email/MailKitEmailService.cs) is the sole implementation of `IEmailService`, called by [OrderPlacedConsumer](../../Services/Notification/Notification.Infrastructure/Events/OrderPlacedConsumer.cs) whenever an `OrderPlacedEvent` arrives off RabbitMQ:

```csharp
public async Task SendOrderConfirmationAsync(
    string toEmail,
    Guid orderId,
    List<OrderLineItem> items,
    decimal total,
    CancellationToken ct)
{
    var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse(_settings.From));
    message.To.Add(MailboxAddress.Parse(toEmail));
    message.Subject = OrderConfirmationEmailTemplate.Subject(orderId);
    message.Body = new TextPart("plain")
    {
        Text = OrderConfirmationEmailTemplate.Body(orderId, items, total)
    };

    using var client = new SmtpClient();
    await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.Auto, ct);
    await client.AuthenticateAsync(_settings.From, _settings.Password, ct);
    await client.SendAsync(message, ct);
    await client.DisconnectAsync(true, ct);
}
```

Every call is a fresh, short-lived `SmtpClient` — connect, authenticate, send, disconnect — rather than a pooled/reused connection; given Notification only sends one email per consumed event, there's no connection-reuse benefit to justify the extra lifecycle complexity. `SecureSocketOptions.Auto` lets MailKit negotiate TLS/StartTLS based on what the target server on `_settings.Port` actually offers, rather than hard-coding an assumption — which is also what lets the exact same code work against smtp4dev (plaintext, port 25, any credentials accepted) in dev and a real provider (implicit TLS or StartTLS, port 587/465, real credentials) in production, with only configuration changing.

`_settings` is `EmailSettings`, bound from flat environment variables rather than a nested config section — see [EmailSettings.cs](../../Services/Notification/Notification.Infrastructure/Settings/EmailSettings.cs):

```csharp
public class EmailSettings
{
    public const string SectionName = "EmailSettings";
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string From { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
```

and wired up directly in [Notification.Api/Program.cs](../../Services/Notification/Notification.Api/Program.cs):

```csharp
builder.Services.AddSingleton(Options.Create(new EmailSettings
{
    Host = builder.Configuration["SMTP_HOST"] ?? "localhost",
    Port = int.TryParse(builder.Configuration["SMTP_PORT"], out var smtpPort) ? smtpPort : 25,
    From = builder.Configuration["SMTP_FROM"] ?? "noreply@shopflow.com",
    Password = builder.Configuration["SMTP_PASSWORD"] ?? string.Empty
}));

builder.Services.AddScoped<IEmailService, MailKitEmailService>();
```

The comment above that block in `Program.cs` explains the flat-keys choice explicitly: it binds "directly from the flat `SMTP_HOST`/`SMTP_PORT`/`SMTP_FROM`/`SMTP_PASSWORD` keys that `.env.example` already provisions for this service, rather than a nested config section." [.env.example](../../.env.example) confirms those four keys:

```
# SMTP (for Notification Service)
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_FROM=noreply@shopflow.com
SMTP_PASSWORD=your-smtp-password
```

### The static template class — `OrderConfirmationEmailTemplate`

[OrderConfirmationEmailTemplate.cs](../../Services/Notification/Notification.Application/Templates/OrderConfirmationEmailTemplate.cs) lives in `Notification.Application` (a `Templates/` folder, one file), not in `Notification.Infrastructure` — the email's *content* is application-level knowledge (what an order confirmation says), while *how it's transmitted* is an infrastructure concern:

```csharp
public static class OrderConfirmationEmailTemplate
{
    public static string Subject(Guid orderId) =>
        $"Order Confirmation - {orderId}";

    public static string Body(Guid orderId, List<OrderLineItem> items, decimal total)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Thank you for your order {orderId}!");
        sb.AppendLine();
        sb.AppendLine("Order summary:");

        foreach (var item in items)
        {
            var lineTotal = item.UnitPrice * item.Quantity;
            sb.AppendLine(
                $"  - {item.ProductName} x{item.Quantity} @ {item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture)} = {lineTotal.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        sb.AppendLine();
        sb.AppendLine($"Total: {total.ToString("F2", CultureInfo.InvariantCulture)}");
        return sb.ToString();
    }
}
```

It's a plain `static class` with two pure string-building methods — no instance state, no DI registration needed, trivially unit-testable with no mocks at all. `CultureInfo.InvariantCulture` on every `.ToString("F2", ...)` call is deliberate: it guarantees `9.99` renders as `9.99` regardless of the server's locale, never `9,99`. [OrderConfirmationEmailTemplateTests.cs](../../Services/Notification/Notification.Application.Tests/Templates/OrderConfirmationEmailTemplateTests.cs) exercises exactly this shape — subject contains the order ID, body contains the order ID/product names/quantities/formatted total, and an empty-items order still renders a `0.00` total without throwing. The body is currently plain text (`TextPart("plain")` in `MailKitEmailService`) — there is no HTML template variant in the codebase at the time of writing.

`OrderPlacedConsumer` maps the event's `OrderItemDto` list (from `ShopFlow.Shared`) into the Notification-local `OrderLineItem` record before calling the email service — see [IEmailService.cs](../../Services/Notification/Notification.Application/Interfaces/IEmailService.cs):

```csharp
public record OrderLineItem(string ProductName, decimal UnitPrice, int Quantity);

public interface IEmailService
{
    Task SendOrderConfirmationAsync(
        string toEmail, Guid orderId, List<OrderLineItem> items, decimal total, CancellationToken ct);
}
```

keeping `Notification.Application` free of any dependency on the wire-level event contract.

### smtp4dev — the local SMTP catcher

[docker-compose.yml](../../docker-compose.yml) defines smtp4dev as its own service, with a comment stating the intent directly:

```yaml
# Dev-only SMTP catcher — Notification's real .env.example SMTP_* values are for a
# production provider; this local server lets the confirmation email be observed
# (web UI + REST API) without sending anywhere real.
smtp4dev:
  image: rnwood/smtp4dev:v3
  container_name: shopflow-smtp4dev
  ports:
    - "5099:80"
  networks:
    - shopflow-net
```

Only port 80 (the web UI/REST API) is published to the host, at `http://localhost:5099`; smtp4dev's SMTP listener (port 25 inside the container) is reached over the internal `shopflow-net` Docker network by hostname, never published to the host directly. `notification-service`'s own block in the same file depends on it and points at it by that internal hostname:

```yaml
notification-service:
  environment:
    - SMTP_HOST=smtp4dev
    - SMTP_PORT=25
    - SMTP_FROM=${SMTP_FROM}
    - SMTP_PASSWORD=${SMTP_PASSWORD}
  depends_on:
    rabbitmq:
      condition: service_healthy
    smtp4dev:
      condition: service_started
```

So in the full Docker Compose stack, `MailKitEmailService` connects to `smtp4dev:25` — a real SMTP handshake, just against a server that only stores what it receives instead of delivering it. A developer confirms an order-confirmation email actually went out by opening `http://localhost:5099` in a browser, or by hitting its REST API.

### smtp4dev in automated tests

[MailKitEmailServiceTests.cs](../../Services/Notification/Notification.Infrastructure.Tests/Email/MailKitEmailServiceTests.cs) goes one step further than Compose: rather than depending on a long-running smtp4dev container, it spins up its own **disposable** smtp4dev container per test class via the generic `Testcontainers` package (there is no `Testcontainers.Smtp4dev` module), with dynamic port binding so tests never collide with the Compose stack's instance:

```csharp
private readonly IContainer _smtp4Dev = new ContainerBuilder()
    .WithImage("rnwood/smtp4dev:v3")
    .WithPortBinding(25, true)
    .WithPortBinding(80, true)
    .WithWaitStrategy(Wait.ForUnixContainer()
        .UntilHttpRequestIsSucceeded(r => r.ForPort(80).ForPath("/api/messages")))
    .Build();
```

`WithPortBinding(25, true)` / `WithPortBinding(80, true)` bind each container port to a random free host port (the `true` flag), and `GetMappedPublicPort(...)` retrieves whichever port Docker actually assigned:

```csharp
private MailKitEmailService CreateSut() =>
    new(Options.Create(new EmailSettings
    {
        Host = "localhost",
        Port = _smtp4Dev.GetMappedPublicPort(25),
        From = "noreply@shopflow.com",
        Password = "does-not-matter-for-smtp4dev"
    }));
```

The test then sends a real email through `MailKitEmailService` and polls smtp4dev's own REST API until the message shows up, rather than asserting anything about MailKit's internals:

```csharp
private async Task<JsonElement> WaitForDeliveredMessageAsync()
{
    for (var attempt = 0; attempt < 40; attempt++)
    {
        var page = await _managementClient.GetFromJsonAsync<JsonElement>("/api/messages");
        var results = page.GetProperty("results");
        if (results.GetArrayLength() > 0) return results[0];
        await Task.Delay(250);
    }
    throw new TimeoutException("smtp4dev did not report a delivered message in time.");
}
```

```csharp
message.GetProperty("from").GetString().Should().Be("noreply@shopflow.com");
message.GetProperty("to").EnumerateArray().Select(x => x.GetString())
    .Should().Contain("customer@example.com");
message.GetProperty("subject").GetString().Should().Contain(orderId.ToString());
```

A second test in the same file fetches `/api/messages/{id}/plaintext` and asserts the body actually contains the item name and formatted total — proving the full path (template → MIME message → SMTP send → real server receipt) end to end, not just that `SendAsync` didn't throw. This is a textbook case of the project's stated Infrastructure-layer philosophy: "testing infrastructure against real dependencies," per the comment in that same test file, applied to SMTP exactly as `Testcontainers.MsSql`/`Testcontainers.Redis` apply it to SQL Server and Redis elsewhere (see [12-testing-stack.md](./12-testing-stack.md)).

## Gotchas & deviations

- **No HTML email support exists.** `OrderConfirmationEmailTemplate.Body` returns plain text, and `MailKitEmailService` sends it as `TextPart("plain")`. Any future rich-HTML template would need a new `TextPart("html")` (or a multipart alternative body) — nothing in the current code path supports it.
- **`.env.example`'s SMTP values are production placeholders, not what's actually used locally.** `SMTP_HOST=smtp.example.com`, `SMTP_PORT=587` in `.env.example` describe a real provider; `docker-compose.yml` overrides these to `smtp4dev`/`25` for the containerized dev stack. Running `Notification.Api` outside Docker (e.g. via `dotnet run`) would need its own local override to point at smtp4dev's mapped port rather than `.env.example`'s literal values, or it will try to reach a nonexistent `smtp.example.com`.
- **A fresh `SmtpClient` per send, no pooling.** Acceptable given Notification's current volume (one email per consumed `OrderPlacedEvent`), but worth knowing if send volume grows — there is no shared/pooled connection reuse anywhere in `MailKitEmailService`.
- **smtp4dev's SMTP port (25) is never published to the host in Compose** — only the web UI (mapped to host `5099`) is. This is fine for `notification-service`, which talks to it over the internal Docker network, but a developer connecting a local SMTP client from outside Docker would need to add a port mapping for 25 themselves; it isn't there by default.
- **`AuthenticateAsync` is always called, even against smtp4dev**, which accepts any credentials — the Infrastructure.Tests file's own comment notes the password value `"does-not-matter-for-smtp4dev"` is arbitrary for that reason. This is a smtp4dev behavior (accept-all), not something MailKit or ShopFlow's code does specially for tests.
