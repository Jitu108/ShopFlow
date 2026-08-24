# ShopFlow convention skills

Seven of the skills in this directory encode ShopFlow's own coding conventions, distilled from the actual patterns in the codebase (see the [Engineering Skills Blueprint](https://claude.ai/code/artifact/5ae99ec9-f663-4b22-ab1f-4cfd3c094c69) for the full human-readable writeup and learning plan behind each one):

| Skill | Encodes |
|---|---|
| `dotnet-backend-conventions` | Clean Architecture, CQRS/MediatR, EF Core, TDD, MassTransit/RabbitMQ, Redis caching, FluentValidation, and entity mutation patterns (role assignment, etc.) for any .NET service |
| `angular-ngrx-conventions` | Feature-store shape, action groups, entity adapters, and RxJS operator choice for NgRx state |
| `jwt-rest-auth-conventions` | Gateway route auth, per-service policies, claims-based ownership, and Ocelot route authoring |
| `docker-compose-dev` | Local stack topology, health-gated startup order, adding a new service to Compose |
| `angular-material-conventions` | Per-component Material module imports, theming tokens, dialogs, signal-based component state |
| `vitest-component-testing` | TestBed isolation patterns, HttpTestingController, table-driven test cases |
| `notification-email-conventions` | Email template classes, the MailKit send sequence, debugging via smtp4dev |

(`monday-ticket`, also in this directory, is a different kind of skill — a one-shot lookup command rather than a set of conventions — and isn't covered by this note.)

## Using them explicitly

Ask for one by name, or run it as a slash command:

```
/dotnet-backend-conventions
```

or in plain language:

> "Follow the angular-ngrx-conventions skill while you add this."

Do this when you want the convention pinned down before Claude starts — e.g. reviewing what a skill actually says before a large or unfamiliar change, or steering Claude back on track if it's drifted from the pattern mid-task.

## How they get used implicitly

You don't have to invoke these for normal work. Every skill in this directory is listed, with its one-line `description`, in a system reminder Claude sees at the start of a session. Each of these seven descriptions ends in a "Use when…" / "Use whenever…" clause naming the concrete situations it applies to — e.g. `dotnet-backend-conventions` triggers on "writing a command/query/handler, a domain entity, a repository, or tests in a `Services/*/*.{Domain,Application,Infrastructure,Api}` project." When Claude recognizes the current task matches one of those clauses, it loads that skill's file into context on its own, before writing any code — no explicit request needed.

This is why the skills are scoped narrowly and by file location rather than written as generic advice: matching is only as reliable as the trigger clause is specific. If Claude misses an applicable skill or applies the wrong one, the fix is almost always to sharpen that skill's `description`, not to remember to invoke it manually every time.

These seven skills are project-scoped — they live in `ShopFlow/.claude/skills/` and only apply in this repo, not in other projects.
