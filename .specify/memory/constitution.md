# Meister ProPR Backend Constitution

## Core Principles

### I. API-Contract-First

`openapi.json` is the single source of truth for the HTTP contract between the
backend and the Azure DevOps extension; Swashbuckle generates it from C# controller
XML doc comments — every public endpoint MUST carry complete `<summary>`, `<param>`,
and `<response>` XML docs; breaking changes (removed fields, changed types, renamed
paths) require a coordinated version bump and a matching extension update; the
generated `openapi.json` MUST be committed and kept current so the extension can
regenerate its typed TypeScript client via `npm run generate:api`.

### II. Test-First (NON-NEGOTIABLE)

TDD is mandatory — no implementation begins before failing tests are written and
reviewed; Red → Green → Refactor is strictly enforced through the speckit workflow:
`[TEST]` tasks appear first in `tasks.md`, are confirmed failing before implementation
begins, implementation drives them green, then a refactor pass cleans up without
changing behaviour; unit tests use xUnit + NSubstitute (Moq is prohibited);
integration tests use `WebApplicationFactory<Program>` with in-memory repository
implementations — no real HTTP calls leave the test process; test projects MUST mirror
source structure (`tests/MeisterProPR.{Layer}.Tests/` mirrors
`src/MeisterProPR.{Layer}/`); a feature is not done until all tests pass in CI —
PRs with failing tests MUST NOT be merged.

### III. Container-First Development

The application is always designed and tested assuming it runs in a rootless Linux
container; no Windows-specific APIs (no registry, no COM, no OS-specific paths);
runtime configuration comes exclusively from environment variables and mounted
secrets — `appsettings.json` may carry non-secret defaults only and MUST NOT contain
credentials, connection strings with passwords, or API keys; the image MUST expose
`/healthz` returning `200 OK` when the background worker is alive and the application
is ready to accept requests; a `docker-compose.yml` at the repository root provides the canonical
local dev stack; base images: `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime),
`mcr.microsoft.com/dotnet/sdk:10.0` (build stage).

### IV. Layered Clean Architecture

Four projects with strictly inward-pointing dependencies —
`MeisterProPR.Api` (controllers, middleware, filters) →
`MeisterProPR.Application` (use cases, service interfaces, DTOs) →
`MeisterProPR.Domain` (entities, value objects — zero external NuGet dependencies);
`MeisterProPR.Infrastructure` (in-memory repositories, ADO REST client, Foundry
client — swappable to EF Core/SQL Server later) → `MeisterProPR.Domain`;
Infrastructure MUST NOT depend on Api; Application MUST NOT reference Infrastructure;
Api depends on Application and references Infrastructure only in `Program.cs` for DI
wiring; repository and registry interfaces are defined in Application so the backing
store can be replaced without touching business logic; controllers are thin
orchestrators — business logic in controllers is a violation; complexity beyond this
structure MUST be justified in the plan's Complexity Tracking table.

### V. Security by Default

Every request MUST validate `X-Client-Key` against the `IClientRegistry` (initially
backed by configuration/environment variables, swappable to a persistent store later)
before any business logic runs — unknown or missing keys return `401` immediately; the `X-Ado-Token` header MUST NEVER
be logged, stored,
cached beyond the request lifetime, or included in any response body — it is
forwarded to the ADO API only; Foundry credentials come exclusively from environment
variables or Azure Key Vault references — they MUST NOT be accepted from request data
or committed to source control; Serilog is the logging provider — log statements MUST
NOT include ADO tokens, client keys, Foundry credentials, or personal data;
destructuring policies MUST scrub sensitive fields.

### VI. Background Job Reliability

Jobs are stored via `IJobRepository` (initially an in-memory `ConcurrentDictionary`
— jobs are lost on restart, which is acceptable for MVP; swappable to a persistent
store later without changing business logic); the job record MUST be written (status
`pending`) and made visible to the worker before the `202 Accepted` response is
returned; valid status transitions are `pending` → `processing` → `completed` |
`failed` only; the background worker (`IHostedService`) polls for pending jobs,
transitions them to `processing`, executes the review pipeline, and transitions to
`completed` or `failed`; unhandled exceptions in the worker MUST be caught, logged
as errors, and transition the job to `failed` — they MUST NOT crash the host process;
idempotency: submitting a review for a PR iteration that already has a non-`failed`
job returns the existing `jobId` rather than creating a duplicate.

### VII. Observability

Serilog writes structured JSON to stdout in all non-Development environments
(human-readable console sink in Development); OpenTelemetry OTLP traces are emitted
for all HTTP requests and all background job executions — ADO API calls and Foundry
agent invocations MUST each be wrapped in an `Activity` span with relevant attributes
(PR ID, iteration ID, job ID); `/healthz` returns `200 OK` with a JSON body
indicating worker liveness and application readiness, or `503 Service Unavailable`
on failure; request duration, job queue depth, and job processing duration MUST be
exported via `System.Diagnostics.Metrics` in a Prometheus-compatible format.

### VIII. Code Style & Formatting

`.editorconfig` at the repository root is the single formatting authority for all
languages — no ad-hoc formatting overrides are permitted; a
`dotnet format --verify-no-changes` gate MUST run in CI on every PR and fail the
build if formatting drift is detected; the rules below reflect the `.editorconfig`
settings and MUST be enforced consistently:

**C# rules**

- **No regional comments**: `#region` / `#endregion` blocks are prohibited; use
  XML documentation comments and proper type decomposition to communicate intent
  instead — the same ban applies to any ad-hoc section marker comments used as
  structural organisers (e.g. `// ── Foo ──`).
- **File-scoped namespace declarations** (`namespace Foo.Bar;`) are mandatory in
  every `.cs` file — block-scoped `namespace Foo.Bar { }` is prohibited.
- **`sealed` by default**: every concrete class that is not explicitly designed for
  inheritance MUST be declared `sealed`.
- **Primary constructors** are the preferred form for constructor injection; a
  traditional constructor body is only used when construction-time guard clauses or
  complex initialisation are required.
- **`var` preference**: use `var` when the type is apparent from the right-hand
  side expression; use explicit types when the type cannot be inferred clearly
  from context.
- **`_camelCase` for private instance fields**; no `m_` prefix; `this.` qualification
  is REQUIRED for all instance member access (field, property, method, event) —
  enforced by the `.editorconfig` `dotnet_style_qualification_for_*` rules.
- **Source-generated `[LoggerMessage]`**: all structured log statements outside of
  test projects MUST use `[LoggerMessage]`-attributed `partial` methods on `partial`
  classes — no string-interpolated `logger.LogXxx(...)` calls in production code.
- **XML documentation comments** are required on every public type, constructor,
  method, property, and event with at minimum a `<summary>` tag; controller actions
  MUST additionally carry `<param>` for every parameter and `<response>` for every
  documented HTTP status code.
- **Allman brace style**: opening braces always appear on a new line
  (`csharp_new_line_before_open_brace = all`); `csharp_prefer_braces = true` — no
  braceless single-line bodies.
- **Expression-bodied members**: allowed for accessors, properties, lambdas, and
  indexers; prohibited for constructors, methods, and operators.

**TypeScript / Vue rules**

- Vue SFCs MUST use `<script setup lang="ts">` with the Composition API exclusively;
  Options API is prohibited.
- Exported TypeScript functions, types, and interfaces MUST carry JSDoc `/** … */`
  comments with at minimum a `@description` or summary line — mirroring the XML-doc
  requirement for C#.
- Formatting is governed by the `*.ts` / `*.vue` sections of `.editorconfig`
  (2-space indent for JSON/YAML, 4-space for TypeScript source).

## Technology Stack & Constraints

**Runtime**: .NET 10 (C#), TFM `net10.0` | **Web**: ASP.NET Core MVC |
**Job/client store**: in-memory (`ConcurrentDictionary`) initially; repository
interfaces in Application enable a future swap to EF Core + SQL Server or SQLite |
**Unit tests**: xUnit + NSubstitute (Moq prohibited) |
**Integration tests**: `WebApplicationFactory<Program>` with in-memory repositories |
**Logging**: Serilog — JSON sink (non-Development), Console sink (Development) |
**Tracing**: OpenTelemetry .NET SDK, OTLP exporter |
**API docs**: Swashbuckle → `openapi.json` committed to repository root |
**AI agent**: Microsoft.Agents.AI (Foundry) |
**Container**: `mcr.microsoft.com/dotnet/aspnet:10.0`, Linux rootless |
**Local stack**: Docker Compose (`docker-compose.yml` at repository root) |
**CI**: all `dotnet test` runs must be green before merge.

Performance targets: review job processing SHOULD complete within 120 s for typical
PR sizes (≤50 changed files); `/healthz` MUST respond in under 200 ms.

## Development Workflow

The speckit command sequence is the mandatory development loop for every feature:
`/speckit.specify` → `/speckit.clarify` → `/speckit.plan` → `/speckit.tasks` →
`/speckit.implement`; `tasks.md` always leads with `[TEST]` tasks confirmed failing
before implementation begins; implementation proceeds task by task until all tests
are green; a refactor pass follows each green cycle without changing behaviour.

Local development: `dotnet run --project src/MeisterProPR.Api` (no external
services required), `dotnet test` for the full suite, `dotnet watch --project
src/MeisterProPR.Api` for hot-reload, `docker compose up` for a production-like
container stack.

CI gate: all `dotnet test` runs must pass; `openapi.json` must be regenerated and
committed whenever endpoints change.

The backend repository root MUST contain a `CLAUDE.md` referencing `Requirements.md`
and summarising build commands, the speckit workflow, and the layered architecture —
consult it at the start of every session.

## Governance

This constitution supersedes all other practices; amendments require a written
rationale, an incremented version, an updated `Last Amended` date, and a propagation
pass across all `.specify/templates/` files and `CLAUDE.md`; version policy: MAJOR
for removed or redefined principles, MINOR for new principles or materially expanded
guidance, PATCH for clarifications and wording; every PR description MUST include a
Constitution Check section confirming the eight principles are satisfied or explicitly
justifying any deviation; complexity beyond what the architecture prescribes MUST be
justified in the plan's Complexity Tracking table; for runtime development guidance
refer to `CLAUDE.md` at the repository root.

**Version**: 1.1.0 | **Ratified**: 2026-03-02 | **Last Amended**: 2026-03-21
