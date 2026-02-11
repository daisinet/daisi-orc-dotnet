# Release Automation Setup — daisi-orc-dotnet

This guide walks through configuring the one-click release system. The ORC is the central hub — it hosts the orchestration workflow and the gRPC endpoint that the Manager calls to start a release.

## Detailed Setup Guides

- **[GitPATSetup.md](GitPATSetup.md)** — Step-by-step instructions for creating `SDK_PAT` and `RELEASE_PAT` in the GitHub portal
- **[AzureADSetup.md](AzureADSetup.md)** — Step-by-step instructions for Azure AD app registration, federated credentials, and RBAC role assignments

## Prerequisites

- An Azure App Service provisioned for the ORC (e.g. `daisi-orc`)
- A GitHub PAT with `repo` and `actions` scope (used for cross-repo workflow dispatch) — see [GitPATSetup.md](GitPATSetup.md)
- Azure federated identity (OIDC) configured for GitHub Actions — see [AzureADSetup.md](AzureADSetup.md)

---

## Step 1: Create the GitHub PAT (`RELEASE_PAT`)

This PAT is used by **both** the orchestration workflow (to dispatch to other repos) and the ORC application (to trigger the orchestration workflow via GitHub API).

1. Go to [GitHub Settings > Developer settings > Personal access tokens > Fine-grained tokens](https://github.com/settings/tokens?type=beta)
2. Click **Generate new token**
3. Set:
   - **Token name**: `daisi-release-automation`
   - **Expiration**: Choose an appropriate lifetime (e.g. 90 days, 1 year)
   - **Repository access**: Select these repositories:
     - `daisinet/daisi-orc-dotnet`
     - `daisinet/daisi-sdk-dotnet`
     - `daisinet/daisi-hosts-dotnet`
   - **Permissions**:
     - **Actions**: Read and write (to dispatch workflows and read run status)
     - **Contents**: Read (to checkout repos)
4. Click **Generate token** and copy the value

> **Alternative**: Use a classic PAT with `repo` and `workflow` scopes if fine-grained tokens are not available for your organization.

---

## Step 2: Configure GitHub Repository Secrets

Go to the **daisi-orc-dotnet** repo: **Settings > Secrets and variables > Actions > New repository secret**

| Secret Name | Value | Purpose |
|---|---|---|
| `AZURE_CLIENT_ID` | Your Azure AD app registration client ID | Azure federated identity for deployment |
| `AZURE_TENANT_ID` | Your Azure AD tenant ID | Azure federated identity |
| `AZURE_SUBSCRIPTION_ID` | Your Azure subscription ID | Azure deployment target |
| `AZURE_ORC_WEBAPP_NAME` | The Azure App Service name (e.g. `daisi-orc`) | Deploy target and app settings update |
| `SDK_PAT` | A GitHub PAT with read access to `daisi-sdk-dotnet` | Checkout the SDK repo during build |
| `RELEASE_PAT` | The PAT created in Step 1 | Dispatch workflows to SDK, ORC, and Host repos |

### How to find the Azure identity values

1. **AZURE_CLIENT_ID**: Azure Portal > App registrations > your app > Overview > Application (client) ID
2. **AZURE_TENANT_ID**: Azure Portal > Azure Active Directory > Overview > Tenant ID
3. **AZURE_SUBSCRIPTION_ID**: Azure Portal > Subscriptions > your subscription > Subscription ID

### Setting up Azure federated identity for GitHub Actions

1. Azure Portal > App registrations > your app > Certificates & secrets > Federated credentials
2. Add credential:
   - **Federated credential scenario**: GitHub Actions deploying Azure resources
   - **Organization**: `daisinet`
   - **Repository**: `daisi-orc-dotnet`
   - **Entity type**: Branch
   - **Branch**: `main`
3. This allows the `deploy-orc.yml` and `orchestrate-release.yml` workflows to authenticate with Azure without storing secrets

---

## Step 3: Configure ORC Application Settings

These are the settings the ORC application reads at runtime to call the GitHub API.

### For local development (User Secrets)

Right-click the `Daisi.Orc.Grpc` project > **Manage User Secrets**, then add:

```json
{
  "GitHub": {
    "ReleasePAT": "<the PAT from Step 1>",
    "OrgName": "daisinet"
  }
}
```

### For production (Azure App Service)

In the Azure Portal, go to your ORC App Service > **Configuration > Application settings** and add:

| Setting | Value |
|---|---|
| `GitHub:ReleasePAT` | The PAT from Step 1 |
| `GitHub:OrgName` | `daisinet` |

> The `Daisi:OrcVersion` setting is updated automatically by the `deploy-orc.yml` workflow on each release. You do not need to set it manually.

---

## Step 4: Verify the Setup

### Quick smoke test

1. Go to the **daisi-orc-dotnet** repo > **Actions** tab
2. Select the **Deploy ORC** workflow
3. Click **Run workflow**, enter a test version like `2026.01.01.0000`
4. Verify it builds, deploys, and updates the `Daisi:OrcVersion` app setting

### Full end-to-end test

1. Go to the **daisi-orc-dotnet** repo > **Actions** tab
2. Select the **Orchestrate Release** workflow
3. Click **Run workflow** with:
   - version: `2026.01.01.0000`
   - release_group: `beta`
   - activate: `true`
4. Watch the pipeline: SDK check > ORC deploy > Host release
5. Verify:
   - ORC App Service `Daisi:OrcVersion` = `2026.01.01.0000`
   - Azure blob exists at `releases/2026.01.01.0000/latest-desktop.zip`
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
