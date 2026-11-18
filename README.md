[![Production deployment](https://img.shields.io/github/deployments/sidcogroup/capitecassessment/Production?label=Production%20deployment)](https://github.com/sidcogroup/capitecassessment/deployments/Production)
![Tests](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/sidcogroup/0146d2e1a9352091b7f555a7effb3d24/raw/capitec-tests.json)

# Transactions Dispute Portal – Backend API

## Project description

This repository contains the **backend API** for the *Transactions Dispute Portal*, built for the following assessment brief:

> **2. Transactions Dispute Portal**
> Create a system (front end and back end) for customers to view transactions and dispute them. There must be a historic view of disputed transactions.

The API lets a customer:

- **Sign in securely** with a one-time PIN (OTP) delivered by email.
- **View their accounts and transactions** (paged and filterable).
- **Raise a dispute** against a transaction.
- **See a historic view of their disputes**, including the full status history of each one (`/disputes` and `/transactions/dispute/{reference}/history`).

It also includes a **consultant admin portal API**, which lets a bank consultant view client profiles, accounts and transactions, review disputes, and update dispute status. Every status change is written to the dispute's history.

## Technology stack

| Concern | Technology |
|---|---|
| Runtime / framework | **.NET 10**, ASP.NET Core Web API (controllers) |
| Data access | Entity Framework Core with **Npgsql** (PostgreSQL provider) |
| Database | **PostgreSQL** (not hosted on Azure; schema `cadb`) |
| Authentication | Email OTP, then an HttpOnly, Secure, SameSite=Strict session cookie (`__Host-session`). Role-based authorisation (`Customer`, `Consultant`) |
| OTP delivery | **Azure Communication Services (Email)**, fully integrated |
| API versioning and docs | `Asp.Versioning.Mvc`, Swagger / OpenAPI (Swashbuckle, Development only) |
| Security controls | OTP hashing (HMAC), attempt limits and resend cooldown, per-IP rate limiting on sign-in, input validation that rejects unsafe text (e.g. SQL injection payloads) |
| Hosting | **Azure Container Apps** (containerised, HTTP autoscaling) |
| Packaging | Docker (multi-stage build, non-root user) |
| Infrastructure as code | Bicep (`infra/`) |
| CI/CD | GitHub Actions (`.github/workflows/deploy.yml`) |
| Tests | xUnit project in `src/api/CapitecAssessment.Tests` |

### Architecture notes and deliberate scope decisions

- The API runs as an **Azure Container App** with autoscaling rules available if required. For the assessment it is **scaled to zero** (min replicas 0, max 1), which keeps costs down.
  - **Cold start:** the first request after a period of inactivity takes noticeably longer while the container starts. This is expected.
- OTPs are sent through **Azure Communication Services Email**.
- For the purpose of this assessment we **chose not to use** other Azure services that would normally be preferred in production:
  - **Azure App Configuration** for feature-flag management
  - **Azure Key Vault** for secret management (secrets are Container App secrets, injected as environment variables)
  - **Log Analytics / Application Insights**
- Only two Azure resources are provisioned: the **Container Apps environment** and the **Container App**. That is enough to demonstrate the required functionality. See [infra/README.md](infra/README.md) for deployment details.

## Repository layout

```
src/api/CapitecAssessment        API project (Controllers, Services, DataAccess, Models, Middlewares)
src/api/CapitecAssessment.Tests  Unit and integration tests
docs/database-schema.md          PostgreSQL schema (apply manually)
docs/seed-data.sql               Mock users, accounts and transactions
infra/                           Bicep templates and deployment notes
Dockerfile                       Multi-stage container build
```

## API overview

All routes are versioned: `/api/v1/...`. Everything except `authenticate` (sign-in) and `health` requires a session.

| Area | Endpoints |
|---|---|
| Authenticate | `POST /authenticate` (request OTP), `POST /authenticate/verify-otp`, `GET /authenticate/me`, `POST /authenticate/logout` |
| Users | `GET /users/profile` |
| Accounts | `GET /accounts`, `GET /accounts/{accountNumber}/transactions` |
| Disputes | `POST /transactions/dispute`, `GET /disputes` (history), `GET /transactions/dispute/{reference}/history` |
| Consultant (role `Consultant`) | `GET /consultant/clients`, `/clients/{userId}`, `/clients/{userId}/accounts`, `/accounts/{accountId}/transactions`, `/disputes`, `/clients/{userId}/disputes`, `PUT /consultant/disputes/{reference}` |
| Health | `GET /health`, `GET /health/database` |

When the API runs in the `Development` environment, the interactive Swagger UI is at `/swagger`.

## Running locally with Docker

### Prerequisites

- [Docker](https://www.docker.com/)
- A running **PostgreSQL** instance that you can reach from the container

### 1. Prepare the database

Create an empty database, then apply these files in order:

1. `docs/database-schema.md` (the SQL for the `cadb` schema)
2. `docs/seed-data.sql` (mock users, accounts and transactions)

The seed script creates five customers and one consultant (username `consultant1`). Their email addresses are placeholders that you can swap for your own. See the "ASSESSOR EMAIL SWAP" block at the bottom of the script.

### 2. Build the image

From the repository root:

```bash
docker build -t capitec-assessment-api .
```

### 3. Run the container

You must pass **your own PostgreSQL connection string**. Use Npgsql format.

```bash
docker run --rm -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e "ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;Database=<db>;Username=<user>;Password=<password>" \
  capitec-assessment-api
```

Notes:

- `host.docker.internal` points the container at a PostgreSQL instance running on your machine. Use the real hostname if the database is remote. For a hosted database, add `SSL Mode=Require` to the connection string.
- On Windows PowerShell, put the command on one line or replace `\` with a backtick.
- `ASPNETCORE_ENVIRONMENT=Development` is what lets the app run without Azure:
  - No Azure Communication Services credentials are needed. Emails are **written to the log** instead of sent.
  - An ephemeral OTP hash key is generated, so you don't need to supply `Otp__HashKey`. OTPs issued before a restart become invalid.
  - Swagger UI is enabled and HTTPS redirection is disabled.

### 4. Sign in and get your OTP

1. Open Swagger at <http://localhost:8080/swagger>.
2. Call `POST /api/v1/authenticate` with a seeded user's identifier.
3. **Locally, the OTP is not emailed.** Read it from the container's live log stream (`docker logs -f <container>`, or the terminal where you ran `docker run`).
4. Call `POST /api/v1/authenticate/verify-otp` with the code to receive the session cookie.

> The session cookie is `Secure`. Browsers treat `http://localhost` as a secure context, so it works locally. Non-browser clients such as curl or Postman must be given the cookie explicitly.

### Configuration reference

Configuration comes from `appsettings.json` and can be overridden with environment variables (use `__` for nesting).

| Setting | Purpose | Local | Deployed |
|---|---|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string | **Required** | Container App secret |
| `Otp__HashKey` | Base64 key (at least 32 bytes) used to hash OTPs | Optional in Development | **Required** |
| `Otp__LogCodeToConsole` | Writes the OTP to the log | `true` in Development | Off |
| `Otp__ValidityMinutes`, `Otp__MaxAttempts`, `Otp__ResendCooldownSeconds` | OTP policy | defaults (5, 3, 60) | defaults |
| `AzureCommunicationServices__Email__ConnectionString` | ACS Email connection string | Not needed | Container App secret |
| `AzureCommunicationServices__Email__SenderAddress` | ACS verified sender address | Not needed | Container App env var |
| `RateLimiting__Authentication__PermitLimit` / `WindowSeconds` | Sign-in rate limit per IP | defaults (10 / 60s) | defaults |

## Running without Docker

```bash
cd src/api/CapitecAssessment
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=<db>;Username=<user>;Password=<password>"
dotnet run --launch-profile http     # http://localhost:5129/swagger
```

## Running the tests

```bash
dotnet test CapitecAssessment.slnx
```

## Deployment

Deployment to Azure Container Apps is automated through GitHub Actions and Bicep. See [infra/README.md](infra/README.md) for the pipeline stages, required secrets and one-time setup.
