# Azure Resiliency Monitor

A portfolio demonstration of scheduled Azure App Service monitoring, controlled
restart requests, and a React dashboard. Deterministic mock resources and frontend
demo scenarios let you explore it without an Azure subscription.

The real monitor reads an App Service's **ARM running state**. It does not probe
application HTTP health or collect CPU/memory metrics; demo metrics are synthetic.
Cosmos DB, Service Bus, Storage, and Function App monitors are extension ideas,
not implemented Azure integrations.

## Quick start: dashboard demonstration

Requires Node.js 24+. From a clean clone:

```bash
git clone https://github.com/Maxencejules/azure-resiliency-monitor.git
cd azure-resiliency-monitor/dashboard
npm ci
npm test
npm run build
npm run dev
```

Open <http://localhost:5173/?demo=healthy>. Change the `demo` query parameter to
`healthy`, `degraded`, `outage`, or `recovered` to inspect each state. These scenarios run in
the browser and make no Azure calls. Open <http://localhost:5173/> for the live API.

The dashboard polls every ten seconds, prevents overlapping requests, retains
the last successful cards during refresh failures, and warns when `checkedAt`
is older than two minutes. A newly fetched cached response can still contain
stale observations. Response durations use numeric `responseTimeMs`; zero-valued
metrics remain visible.

## Build and test the backend

Requires the .NET 10 SDK. From the repository root:

```bash
dotnet restore AzureResiliencyMonitor.Functions/AzureResiliencyMonitor.Functions.csproj
dotnet restore AzureResiliencyMonitor.sln
dotnet build AzureResiliencyMonitor.sln --no-restore
dotnet test AzureResiliencyMonitor.sln --no-build --no-restore
```

The Functions application uses the isolated worker model. Tests exercise cached
HTTP reads, repeated unhealthy checks, recovery cooldowns, failed and throwing
recovery, concurrent checks, cancellation, and deterministic mocks. Dashboard
tests cover loading, errors/retry, empty snapshots, stale observations, polling
cleanup, malformed responses, numeric durations, and demos. GitHub Actions builds
and tests both applications from a clean checkout.

Azure adapter tests use the real SDK with an in-memory HTTP transport and a fake
token. They verify ARM request paths, restart flags, failed reads/restarts, and
cancellation during I/O, without contacting Azure.

## Run the connected local demo

Install Azure Functions Core Tools v4 and Azurite. Development mode requires no
Azure login. Copy the configuration template:

```bash
# Bash; on Windows use Copy-Item with the same source and destination.
cp AzureResiliencyMonitor.Functions/local.settings.example.json AzureResiliencyMonitor.Functions/local.settings.json
```

Start Azurite from the repository root:

```bash
azurite --location .local/azurite
```

In a second terminal:

```bash
cd AzureResiliencyMonitor.Functions
func start
```

In a third terminal, run `npm run dev` from `dashboard` and open
<http://localhost:5173/>. The Vite server proxies `/api` to
`http://127.0.0.1:7071`; use `VITE_API_PROXY_TARGET` to change that destination.
For a deployed dashboard, set `VITE_API_BASE_URL` at build time and configure
the API's authentication and allowed origins for that deployment.

The first snapshot appears after the first scheduled check. An empty list on a
new host means no observation has been collected yet.

## Checks, recovery, and configuration

Only the timer runs checks and evaluates recovery. `GET /api/health/current`
reads the latest snapshot and never initiates a health check or restart.

| Application setting | Default | Meaning |
|---|---|---|
| `AZURE_FUNCTIONS_ENVIRONMENT` | `Development` in the example | Development registers mocks; other environments use the App Service monitor |
| `MonitoredResources` | Empty | Comma-separated IDs; development supplies three mocks when empty |
| `RecoveryEnabled` | `false` | Explicitly enables restart requests for unhealthy resources |
| `RecoveryCooldownSeconds` | `300` | Positive delay between attempts, including failures |
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` in the example | Timer storage; use Azurite locally |

Checks run once a minute. Recovery metadata includes `Disabled`, `Cooldown`,
`Started`, and `Failed`. `Started` means the restart request was accepted; health
remains unhealthy until a later check observes improvement. Degraded and unknown
observations do not trigger recovery.

Mock names ending in `/healthy`, `/degraded`, `/unhealthy`, or `/recovery-fails`
produce fixed observations. Recovery for `/recovery-fails` returns failure.
For example, enable recovery in a local demo with:

```json
"MonitoredResources": "mock://app/unhealthy,mock://app/recovery-fails",
"RecoveryEnabled": "true",
"RecoveryCooldownSeconds": "300"
```

Snapshots and cooldowns are **process-local** and reset on host restart. Checks
serialize within that process. This demo does not coordinate recovery across
scaled-out hosts; production deployment needs durable coordination.

For real App Service checks, use an ARM resource ID such as
`/subscriptions/{id}/resourceGroups/{group}/providers/Microsoft.Web/sites/{name}`
and an identity available to `DefaultAzureCredential`. Start with read permissions
and recovery disabled; restart permission is required when enabling recovery.
Keep local settings untracked. Real Azure recovery and cloud deployment are
outside the automated mock tests.

## API contract

| Endpoint | Behavior |
|---|---|
| `GET /api/health` | Basic host health |
| `GET /api/health/current` | Array of cached observations; empty before the first check |

Example observation:

```json
{
  "serviceName": "unhealthy",
  "resourceId": "mock://app/unhealthy",
  "serviceType": "AppService",
  "status": "Unhealthy",
  "message": "Deterministic mock: Unhealthy",
  "checkedAt": "2026-09-29T12:00:00Z",
  "responseTime": "00:00:00.1200000",
  "responseTimeMs": 120,
  "metadata": { "mock": true, "recoveryOutcome": "Disabled" }
}
```

The duration string remains for compatibility. Use `responseTimeMs` for calculations.

## Structure

- `AzureResiliencyMonitor.Core`: observations, policy, orchestration, real and mock monitors.
- `AzureResiliencyMonitor.Functions`: timer, HTTP endpoints, dependency/configuration setup.
- `AzureResiliencyMonitor.Tests`: backend and HTTP contract regressions.
- `dashboard`: React/TypeScript app, Vite build, and Vitest tests.

Author: [Maxence Jules](https://github.com/Maxencejules).
License: MIT.
The [.NET isolated worker guide](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)
documents the Functions runtime and deployment model.
