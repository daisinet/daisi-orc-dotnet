# Release Automation Setup — ORC Repo

This guide walks through configuring the one-click release system. The ORC is the central hub — it hosts the orchestration workflow and the gRPC endpoint that the Manager calls to start a release.

> **Naming conventions used in this guide:**
> Values like `daisinet`, `daisi-orc`, and `daisi-orc-dotnet` are examples from our deployment. Replace them with your own organization name, App Service name, and repo names.

## Detailed Setup Guides

- **[GitPATSetup.md](GitPATSetup.md)** — Step-by-step instructions for creating `SDK_PAT` and `RELEASE_PAT` in the GitHub portal
- **[AzureADSetup.md](AzureADSetup.md)** — Step-by-step instructions for Azure AD app registration, federated credentials, and RBAC role assignments

## Prerequisites

- An Azure App Service provisioned for the ORC (e.g. `daisi-orc`)
- A GitHub PAT with `repo` and `actions` scope (used for cross-repo workflow dispatch) — see [GitPATSetup.md](GitPATSetup.md)
- Azure federated identity (OIDC) configured for GitHub Actions — see [AzureADSetup.md](AzureADSetup.md)

---

## Step 1: Create the GitHub PAT (`RELEASE_PAT`)

This PAT is used by **both** the orchestration workflow (to dispatch to other repos) and the ORC application (to trigger the orchestration workflow via GitHub API). See [GitPATSetup.md](GitPATSetup.md) for detailed instructions.

Summary:
1. Create a fine-grained token with **Actions: Read/Write** and **Contents: Read** permissions on all three repos (ORC, SDK, Hosts)
2. Store it as the `RELEASE_PAT` GitHub Actions secret
3. Also add it as an Azure App Service setting (`GitHub:ReleasePAT`)

---

## Step 2: Configure GitHub Repository Secrets

Go to your ORC repo: **Settings > Secrets and variables > Actions > New repository secret**

| Secret Name | Description | Example |
|---|---|---|
| `AZURE_CLIENT_ID` | The Application (client) ID from your Azure AD app registration. Identifies the service principal for OIDC login. | `a1b2c3d4-e5f6-...` |
| `AZURE_TENANT_ID` | The Directory (tenant) ID from your Azure AD tenant. Identifies which Azure AD instance to authenticate against. | `f7e8d9c0-b1a2-...` |
| `AZURE_SUBSCRIPTION_ID` | The ID of the Azure subscription containing your resources. Found in the Azure Portal under Subscriptions. | `1a2b3c4d-5e6f-...` |
| `AZURE_ORC_WEBAPP_NAME` | The name of the Azure App Service where the ORC is deployed. This is the name shown in the portal, not a GUID (same as the subdomain in `<name>.azurewebsites.net`). | `daisi-orc` |
| `SDK_PAT` | A GitHub PAT with **Contents: Read** access to the SDK repo. Used by `actions/checkout` to clone the SDK during builds. See [GitPATSetup.md](GitPATSetup.md). | `github_pat_...` |
| `RELEASE_PAT` | A GitHub PAT with **Actions: Read/Write** and **Contents: Read** access to the ORC, SDK, and Hosts repos. Used to dispatch workflows cross-repo. See [GitPATSetup.md](GitPATSetup.md). | `github_pat_...` |

See [AzureADSetup.md](AzureADSetup.md) for how to find the Azure identity values and set up federated credentials.

---

## Step 3: Configure ORC Application Settings

These are the settings the ORC application reads at runtime to call the GitHub API when a user triggers a release.

### For local development (User Secrets)

Right-click the ORC gRPC project > **Manage User Secrets**, then add:

```json
{
  "GitHub": {
    "ReleasePAT": "<your RELEASE_PAT token>",
    "OrgName": "<your-org>"
  }
}
```

- `ReleasePAT` — the same PAT stored as the `RELEASE_PAT` GitHub secret. The ORC uses it to call the GitHub REST API.
- `OrgName` — your GitHub organization or account name (e.g. `daisinet`). Used to construct the API URL: `repos/<OrgName>/<repo>/actions/workflows/...`

### For production (Azure App Service)

In the Azure Portal, go to your ORC App Service > **Configuration > Application settings** and add:

| Setting | Description | Example |
|---|---|---|
| `GitHub:ReleasePAT` | The same PAT as the `RELEASE_PAT` GitHub secret | `github_pat_...` |
| `GitHub:OrgName` | Your GitHub organization or account name | `daisinet` |

> The `Daisi:OrcVersion` setting is updated automatically by the `deploy-orc.yml` workflow on each release. You do not need to set it manually.

---

## Step 4: Verify the Setup

### Quick smoke test

1. Go to your ORC repo > **Actions** tab
2. Select the **Deploy ORC** workflow
3. Click **Run workflow**, enter a test version like `2026.01.01.0000`
4. Verify it builds, deploys, and updates the `Daisi:OrcVersion` app setting

### Full end-to-end test

1. Go to your ORC repo > **Actions** tab
2. Select the **Orchestrate Release** workflow
3. Click **Run workflow** with:
   - version: `2026.01.01.0000`
   - release_group: `beta`
   - activate: `true`
4. Watch the pipeline: SDK check > ORC deploy > Host release
5. Verify:
   - ORC App Service `Daisi:OrcVersion` matches the version
   - Azure blob exists at the versioned path
   - CosmosDB Releases container has a record with the correct version

---

## Workflows Reference

| Workflow | File | Trigger | What it does |
|---|---|---|---|
| **Deploy ORC** | `deploy-orc.yml` | `workflow_dispatch` (version) | Builds ORC, deploys to App Service, sets OrcVersion |
| **Orchestrate Release** | `orchestrate-release.yml` | `workflow_dispatch` (version, group, notes, activate) | Checks SDK > deploys ORC > releases Host |

---

## Troubleshooting

**"Resource not accessible by integration"** when dispatching workflows
- The `RELEASE_PAT` doesn't have access to the target repo. Verify the token's repository access includes all three repos.

**"Not Found" (404)** on workflow dispatch
- The workflow file doesn't exist on the default branch. Ensure `orchestrate-release.yml` and `deploy-orc.yml` are merged to `main`.

**Azure deployment fails with "AADSTS700024"**
- The federated credential entity (branch/environment) doesn't match. Check that the federated credential is set for the `main` branch.

**ORC returns "GitHub:ReleasePAT configuration is required"**
- The app setting isn't configured. Add `GitHub:ReleasePAT` to the App Service configuration or user secrets.
