# Tally

Tally is a small expense claims app for teams. An employee submits an expense, someone with the approver role approves or rejects it, and the approved spend can be totalled by category. It has a JSON API with an OpenAPI document and a server-rendered web UI, and both sit on the same application services.

It is built with ASP.NET Core on .NET 10 (the current LTS release), EF Core 10, SQLite for local work and PostgreSQL for anything shared.

## Running it

You need the .NET 10 SDK.

```bash
dotnet run --project src/Tally.Web
```

The Development profile creates a SQLite file, applies migrations and seeds two demo users. Open http://localhost:5080 for the UI and http://localhost:5080/scalar for interactive API docs. Both demo accounts use the password `tally-demo-password`:

- `employee@tally.local` submits claims
- `approver@tally.local` reviews them

To run on Postgres in containers instead:

```bash
docker compose up --build
```

The app is then on http://localhost:8080 with the same demo users. The compose file is for a laptop; its passwords and signing key are placeholders.

## Tests

```bash
dotnet test
```

That runs 26 unit tests and 22 integration tests. The integration tests start the real app in memory with `WebApplicationFactory` and talk to it over HTTP, against a real database. By default that is an in-memory SQLite database. Point them at a Postgres server and the same suite runs there, creating and dropping a throwaway database:

```bash
TALLY_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres" \
  dotnet test --project tests/Tally.IntegrationTests
```

CI does both on every push, then builds the Docker image.

## Using the API

```bash
TOKEN=$(curl -s -X POST localhost:5080/api/auth/token \
  -H 'content-type: application/json' \
  -d '{"email":"employee@tally.local","password":"tally-demo-password"}' | jq -r .accessToken)

curl -s -X POST localhost:5080/api/claims \
  -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d '{"description":"Taxi to the airport","category":"Travel","amount":38.50,"currency":"USD","incurredOn":"2026-11-18"}'
```

| Method | Path | Who |
| --- | --- | --- |
| POST | `/api/auth/register`, `/api/auth/token` | anyone |
| GET | `/api/auth/me` | signed in |
| GET, POST | `/api/claims` | signed in, own claims |
| GET | `/api/claims/{id}` | owner or approver |
| POST | `/api/claims/{id}/withdraw` | owner |
| POST | `/api/claims/{id}/approve`, `/reject` | approver, not on their own claim |
| GET | `/api/reviews` | approver |
| GET | `/api/reports/approved-totals?from=&to=` | approver |
| GET | `/health`, `/openapi/v1.json` | anyone |

Errors come back as RFC 9457 problem details with a stable `code` field, such as `claim.self_review` or `claim.invalid_transition`, so a client can branch on the code instead of parsing the message.

## Design decisions

The choices worth explaining, and the reasons behind them.

**Three projects, not a full Clean Architecture template.** `Tally.Domain` has the entities and rules and depends on nothing. `Tally.Web` holds the API endpoints, Razor Pages, application services and EF Core. Splitting Web into Application, Infrastructure and Api projects would add ceremony without protecting anything at this size; the boundary that matters is that business rules can't reach for a database or an HTTP context, and the domain project enforces that by having no references. If a second host appeared (a worker processing receipts, say) the services and data folders would move into their own project at that point.

**The rules live in the aggregate, and failures are values.** A claim only changes state through `Submit`, `Approve`, `Reject` and `Withdraw` on `ExpenseClaim`. Those methods return a `Result` carrying an `Error` with a code and a kind (validation, forbidden, conflict and so on) instead of throwing. Exceptions are kept for things that are actually exceptional. The API maps error kinds to status codes in one place, and the unit tests can cover every transition without a database. Request DTOs also carry data annotations, checked by the validation built into .NET 10 minimal APIs, so callers get field-level 400s early. That duplicates a few checks on purpose: the annotations shape the request, the domain decides what is allowed.

**Money is stored as an integer count of minor units.** 12.34 USD is stored as 1234 with the currency code beside it. That avoids floating point drift, rejects amounts with too many decimal places for the currency (JPY has none), and sums identically on SQLite, which has no real decimal type, and on Postgres.

**Two approvers can't both act on the same claim.** Each claim has a version that changes on every transition and is configured as an EF Core concurrency token. If two reviewers load the same pending claim and both act, the second save fails and the API returns 409 with `claim.concurrent_update`. There is an integration test that forces this race.

**Cookies for pages, bearer tokens for the API, never crossed.** The Razor Pages use a cookie (HttpOnly, SameSite Strict, antiforgery tokens on every form). Everything under `/api` requires a policy that only accepts JWT bearer tokens, so a browser that happens to hold a session cookie can't be tricked into calling the API. Passwords are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2) and tokens are signed with HMAC-SHA256. The full ASP.NET Core Identity stack isn't used because two roles and a password don't need its tables and UI; in a real company the app would accept tokens from an identity provider such as Entra ID or Auth0 and drop local passwords entirely. Self-registration only ever creates employees, and the token endpoint and login page are rate limited per IP.

**Provider-neutral data access with a migration history per provider.** Services depend on an abstract `TallyDbContext`. `SqliteTallyDbContext` and `PostgresTallyDbContext` are empty subclasses that exist so each provider keeps its own migrations, which is one of the two approaches the EF Core docs describe for targeting more than one database with one model. Configuration picks the provider. Migrations can be applied at startup for local runs and demos; in production they would run as a deploy step (an EF migration bundle) so several instances don't race to migrate.

**State that has to outlive a container lives in the database.** ASP.NET Core signs cookies and antiforgery tokens with data protection keys, which by default sit in the container's file system and vanish on redeploy, signing everyone out. Here they are stored in the database, so a container restart doesn't sign anyone out and more than one instance can share the keys.

**Testing against real databases, not mocks.** EF Core's in-memory provider doesn't behave like a relational database (no constraints, no concurrency tokens, different query translation), so the integration tests use real SQLite and, in CI, real Postgres. The only test double is `FakeTimeProvider`, used by the unit tests for the 90-day submission window. Time comes from `TimeProvider` everywhere, never `DateTime.Now`.

**Razor Pages rather than a SPA.** The UI is four pages of forms and tables. Server rendering keeps it small, needs no JavaScript build, and calls the same `ClaimService` as the API, so the two can't drift. The API and OpenAPI document are there for a React or mobile client if one is ever needed.

**Smaller choices.** Central package management keeps versions in one file. Warnings fail the build. Logging uses `[LoggerMessage]` source-generated methods, and outside Development it writes one JSON object per line for a log pipeline. IDs are UUID version 7, which sort by creation time and index well. The Docker image restores in its own layer and runs as the non-root user the .NET images provide.

## Next steps

- Encrypt the data protection keys at rest with a certificate or a cloud key vault. They are stored unencrypted today and the app logs a warning saying so.
- Receipt uploads to blob storage, with a size limit and content-type checks.
- An audit trail of who changed what, written in the same transaction as the change.
- Refresh tokens, or better, hand authentication to an identity provider.
- Multi-currency reporting with exchange rates. Totals are grouped by currency for now and never converted.
- TLS is expected to end at a load balancer or reverse proxy, so the app doesn't redirect to HTTPS itself.

On the first start against an empty database EF Core logs one error while checking for its migrations history table. That is EF's normal behaviour before the table exists, not a failure.

## Layout

```
src/
  Tally.Domain/            entities, value objects, Result and Error; no dependencies
  Tally.Web/
    Api/                   minimal API endpoints and problem-details mapping
    Application/           ClaimService, AuthService, request and response types
    Infrastructure/Auth/   cookie and JWT setup, token issuing, policies
    Infrastructure/Data/   DbContext, provider subclasses, migrations, startup migration and seeding
    Pages/                 Razor Pages UI
tests/
  Tally.UnitTests/         domain rules
  Tally.IntegrationTests/  the app over HTTP against SQLite or Postgres
```
