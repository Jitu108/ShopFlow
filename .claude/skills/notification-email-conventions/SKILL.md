---
name: notification-email-conventions
description: How outbound email is composed and sent from Notification.Api — MailKit SMTP client, static template classes, and the local smtp4dev catcher for verifying an email without sending it anywhere real. Use when adding a new email type or debugging why an email did or didn't arrive in dev.
---

# Notification / Email Conventions

## Composing an email

Each email type is a `static` template class in `Notification.Application/Templates` (see `OrderConfirmationEmailTemplate`) with two static methods: `Subject(...)` and `Body(...)`, taking only plain data (ids, line items, totals) — no MailKit/MimeKit types leak into the template class. Body text is built with a `StringBuilder`, and every currency value is formatted with `.ToString("F2", CultureInfo.InvariantCulture)` — never string-interpolate a `decimal` directly, it won't consistently render two decimal places across locales.

## Sending an email

`MailKitEmailService` (`Notification.Infrastructure/Email`) is the only place that touches `MailKit`/`MimeKit` — it implements `IEmailService`, builds a `MimeMessage` from a template's `Subject`/`Body`, and does the connect/authenticate/send/disconnect sequence against `EmailSettings` (bound from `SMTP_HOST`/`PORT`/`FROM`/`PASSWORD`). Consumers (e.g. `OrderPlacedConsumer` reacting to `OrderPlacedEvent`) depend on `IEmailService`, never on `MailKitEmailService` or MailKit types directly.

## Adding a new email type

1. Add a new static template class with `Subject(...)`/`Body(...)` taking plain data.
2. Add the corresponding method to `IEmailService` and implement it in `MailKitEmailService`.
3. Trigger it from wherever the business event already exists (a MassTransit consumer, most often) — see [[dotnet-backend-conventions]] for the consumer/retry pattern.
4. Add a template test (see `OrderConfirmationEmailTemplateTests.cs`) asserting on the exact rendered subject/body string, not just "doesn't throw."

## Debugging an email locally

Local dev never talks to the real SMTP settings in `.env.example` (those are for production) — Compose points `notification-service` at `smtp4dev` instead (see [[docker-compose-dev]]). To check whether an email was actually sent and what it contained:

1. Open the smtp4dev web UI at `http://localhost:5099` and look for the message — this is the primary way to verify an email "arrived" in dev, there is no real inbox to check.
2. If nothing shows up, check `notification-service`'s logs first for a MailKit connect/auth exception before assuming the consumer never ran — a thrown exception in `SendOrderConfirmationAsync` will be retried per the consumer's message-retry policy, not silently dropped.
3. If the message appears but looks wrong, the bug is almost always in the template class (`Subject`/`Body`), not the MailKit plumbing — check that first.
