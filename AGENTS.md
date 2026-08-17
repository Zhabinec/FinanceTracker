# Finance Tracker Codex Instructions

## Required context

Before planning or implementing any non-trivial task, read `PROJECT.md`.

`PROJECT.md` is the source of truth for:

- product purpose;
- developer learning goals;
- MVP scope;
- architectural boundaries;
- business rules;
- implementation order.

Do not silently contradict or expand it.

## Working approach

For non-trivial tasks:

1. Inspect the relevant code.
2. Restate the task and expected behavior.
3. Identify business invariants and failure cases.
4. Propose a short implementation plan.
5. Explain important architectural decisions.
6. Implement the smallest complete vertical change.
7. Add or update relevant tests.
8. Run the required checks.
9. Explain the resulting request and data flow.

Codex may generate complete implementations and boilerplate.

The learning goal is understanding architecture and backend behavior,
not manually typing every line.

## Constraints

- Do not introduce microservices.
- Do not add MediatR, AutoMapper, generic repositories, Redis,
  RabbitMQ or new production dependencies without explaining the
  concrete problem they solve and receiving approval.
- Keep controllers thin.
- Keep Domain independent from framework and infrastructure code.
- Use EF Core migrations for database schema changes.
- Do not commit secrets.
- Do not implement future roadmap stages without being asked.
- Preserve cancellation through asynchronous backend operations.
- Use decimal for money.
- Enforce user data ownership on the backend.

## Verification

For backend changes run:

- `dotnet build`
- `dotnet test`

For frontend changes run:

- `npm run lint`
- `npm run build`

For Docker changes run:

- `docker compose config`

Never report a check as passing unless it was actually executed.

## Communication

After implementation, explain:

- what changed;
- why this design was selected;
- which alternatives were rejected and why;
- how the request moves through the system;
- what the developer should understand for an interview.