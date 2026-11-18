# Deployment

GitHub Actions (`.github/workflows/deploy.yml`) deploys the API to Azure Container Apps.

Stages: `test` -> `build` (image to ACR, tagged with the commit SHA) -> `deploy-dev` (Bicep lint, validate, what-if; **no changes applied**) -> `deploy-prod` (real deployment, main/manual runs only).

Everything is deployed into a single resource group (the assessment uses one environment).

## Container App sizing (cost)
0.25 vCPU / 0.5 Gi, min replicas 0, max replicas 1 (scales to zero; first request after idle is a cold start).

## One-time setup
1. Create the resource group and set `RESOURCE_GROUP` in `.github/workflows/deploy.yml`.
2. Create an Azure Container Registry (admin user enabled).
3. Create a service principal with Contributor and User Access Administrator on the resource group; store its JSON as `AZURE_CREDENTIALS`.
4. Add GitHub environments `Development` and `Production` (optionally require a reviewer on `Production`).
5. Add repository secrets:
   `AZURE_CREDENTIALS`, `AZURE_CONTAINER_REGISTRY` (name only), `AZURE_REGISTRY_USERNAME`, `AZURE_REGISTRY_PASSWORD`,
   `NEONDB_CONNECTIONSTRING`, `OTP_HASHKEY` (base64, >= 32 bytes), `AZURE_EMAIL_CONNECTIONSTRING`, `AZURE_EMAIL_SENDER_ADDRESS`.
6. Apply `docs/database-schema.md` and `docs/seed-data.sql` to the database manually (not part of the pipeline).

Only two Azure resources are provisioned: a Container Apps environment and the Container App. Secrets are passed from GitHub Actions as Bicep secure parameters and stored as Container App secrets (exposed to the app as environment variables). No Key Vault, Log Analytics or Application Insights.
