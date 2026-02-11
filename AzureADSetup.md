# Azure AD / Entra ID Setup — daisi-orc-dotnet

The ORC's GitHub Actions workflows authenticate to Azure using **workload identity federation** (OIDC). This eliminates the need to store Azure credentials as GitHub secrets — instead, GitHub Actions presents a short-lived token that Azure trusts based on a pre-configured trust relationship.

This guide walks through creating the Azure AD app registration, configuring federated credentials for GitHub Actions, assigning RBAC roles on the ORC App Service, and storing the resulting IDs as GitHub secrets.

---

## Overview

```
GitHub Actions (deploy-orc.yml)
    |
    |-- presents OIDC token (signed by GitHub)
    |
    v
Azure AD / Entra ID
    |-- verifies token matches federated credential
    |   (org: daisinet, repo: daisi-orc-dotnet, branch: main)
    |
    |-- issues Azure access token for the app registration's service principal
    |
    v
Azure App Service (daisi-orc)
    |-- service principal has Contributor role
    |-- workflow deploys code and updates app settings
```

---

## Step 1: Create an App Registration

If you already have an app registration for DAISI deployments, skip to Step 2.

1. Sign in to the [Azure Portal](https://portal.azure.com)
2. Search for **"App registrations"** in the top search bar and select it
3. Click **+ New registration**
4. Fill in the form:
   - **Name**: `daisi-github-deployments` (or any descriptive name)
   - **Supported account types**: **Accounts in this organizational directory only** (single tenant)
   - **Redirect URI**: Leave blank (not needed for workload identity)
5. Click **Register**
6. On the app's **Overview** page, note these values — you'll need them as GitHub secrets:
   - **Application (client) ID** — this becomes `AZURE_CLIENT_ID`
   - **Directory (tenant) ID** — this becomes `AZURE_TENANT_ID`

---

## Step 2: Add a Federated Credential for daisi-orc-dotnet

This tells Azure AD to trust OIDC tokens from GitHub Actions when they come from the `daisi-orc-dotnet` repo on the `main` branch.

1. In the Azure Portal, go to **App registrations** > select your app (e.g. `daisi-github-deployments`)
2. In the left sidebar, click **Certificates & secrets**
3. Click the **Federated credentials** tab
4. Click **+ Add credential**
5. Select **Federated credential scenario**: **GitHub Actions deploying Azure resources**
6. Fill in the form:
   - **Organization**: `daisinet`
   - **Repository**: `daisi-orc-dotnet`
   - **Entity type**: **Branch**
   - **GitHub branch name**: `main`
   - **Name**: `daisi-orc-dotnet-main` (a label for this credential)
   - **Description**: `Deploy ORC from main branch` (optional)
   - **Issuer**: Should be pre-filled with `https://token.actions.githubusercontent.com`
   - **Subject identifier**: Should be pre-filled with `repo:daisinet/daisi-orc-dotnet:ref:refs/heads/main`
7. Click **Add**

### Why `main` branch?

Both `deploy-orc.yml` and `orchestrate-release.yml` are triggered via `workflow_dispatch`, which runs on the default branch. The federated credential must match the branch the workflow runs on. If you later add tag-triggered workflows, you'll need a separate federated credential for that entity type.

> **Important**: Each combination of (org, repo, entity type, value) needs its own federated credential. You can add up to 20 per app registration.

---

## Step 3: Find Your Subscription ID

1. In the Azure Portal, search for **"Subscriptions"** in the top search bar
2. Select the subscription that contains your ORC App Service
3. On the subscription's **Overview** page, copy the **Subscription ID** — this becomes `AZURE_SUBSCRIPTION_ID`

---

## Step 4: Create a Service Principal and Assign RBAC Roles

The app registration needs permission to deploy to the ORC App Service and update its settings.

### 4a: Verify the service principal exists

When you created the app registration, Azure automatically created a corresponding service principal. Verify:

1. In the Azure Portal, search for **"Enterprise applications"**
2. Search for your app name (e.g. `daisi-github-deployments`)
3. You should see it listed — click on it to confirm

### 4b: Assign Contributor role on the ORC App Service

1. In the Azure Portal, go to **App Services** > select your ORC app (e.g. `daisi-orc`)
2. In the left sidebar, click **Access control (IAM)**
3. Click **+ Add** > **Add role assignment**
4. On the **Role** tab:
   - Search for **Contributor**
   - Select **Contributor** and click **Next**
5. On the **Members** tab:
   - **Assign access to**: **User, group, or service principal**
   - Click **+ Select members**
   - Search for your app registration name (e.g. `daisi-github-deployments`)
   - Select it and click **Select**
6. Click **Review + assign** > **Review + assign** again

The **Contributor** role grants the service principal permission to:
- Deploy code to the App Service
- Update application settings (`az webapp config appsettings set`)
- Restart the app

### 4c: Assign Contributor role on the Resource Group (alternative)

If you prefer a broader scope (e.g. you'll add more App Services later), you can assign the role at the resource group level instead:

1. Go to **Resource groups** > select the group containing your ORC App Service
2. Follow the same **Access control (IAM)** > **Add role assignment** steps as above

> **Principle of least privilege**: Scoping to the individual App Service is more secure. Only use the resource group scope if multiple services in the group need deployment access.

---

## Step 5: Store Azure IDs as GitHub Secrets

1. Go to [github.com/daisinet/daisi-orc-dotnet](https://github.com/daisinet/daisi-orc-dotnet)
2. Click **Settings** > **Secrets and variables** > **Actions**
3. Add each secret by clicking **New repository secret**:

| Secret Name | Value | Where to find it |
|---|---|---|
| `AZURE_CLIENT_ID` | Application (client) ID | Azure Portal > App registrations > your app > Overview |
| `AZURE_TENANT_ID` | Directory (tenant) ID | Azure Portal > App registrations > your app > Overview |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID | Azure Portal > Subscriptions > your subscription > Overview |
| `AZURE_ORC_WEBAPP_NAME` | App Service name (e.g. `daisi-orc`) | Azure Portal > App Services > your ORC app > Overview > Name field |

> **Note**: `AZURE_ORC_WEBAPP_NAME` is the **name** of the App Service, not a GUID. It's the same value that appears in the URL: `https://<name>.azurewebsites.net`.

---

## Step 6: Verify the Setup

### Test Azure login from GitHub Actions

1. Go to the **daisi-orc-dotnet** repo > **Actions** tab
2. Select the **Deploy ORC** workflow
3. Click **Run workflow**, enter a version like `2026.01.01.0000`
4. Watch the **Azure Login** step — it should succeed without errors
5. If it fails, check:
   - The federated credential matches `repo:daisinet/daisi-orc-dotnet:ref:refs/heads/main`
   - The `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` secrets are correct
   - The service principal has `Contributor` role on the App Service

### Test full deployment

If the Azure Login step passes, the workflow should:
1. Build and publish the ORC
2. Deploy to the App Service
3. Update the `Daisi:OrcVersion` app setting

Verify in the Azure Portal:
- **App Services** > your ORC > **Overview**: should show a recent deployment
- **Settings** > **Environment variables** > **App settings**: `Daisi:OrcVersion` should match the version you entered

---

## Troubleshooting

### "AADSTS700024: Client assertion is not within its valid time range"
- The GitHub Actions runner's clock may be skewed, or the workflow ran on a branch/tag that doesn't match the federated credential. Verify the federated credential's entity type and value.

### "AADSTS70021: No matching federated identity record found"
- The federated credential's subject doesn't match the token GitHub presented. Check:
  - **Organization** matches `daisinet`
  - **Repository** matches `daisi-orc-dotnet`
  - **Entity type** is `Branch` and value is `main`
  - If using a different trigger (tag, environment, PR), you need a separate federated credential

### "AuthorizationFailed: The client does not have authorization to perform action"
- The service principal doesn't have the right role. Go to the App Service > **Access control (IAM)** > **Role assignments** tab and verify the app registration appears with the `Contributor` role.

### "The subscription is not found" or "SubscriptionNotFound"
- The `AZURE_SUBSCRIPTION_ID` secret is wrong, or the service principal doesn't have any role in that subscription. Double-check the value and add at least a Reader role at the subscription level if needed.

### "WebApp 'daisi-orc' not found"
- The `AZURE_ORC_WEBAPP_NAME` secret doesn't match the actual App Service name. The name is case-sensitive and must match exactly.

---

## Architecture Reference

| Azure Resource | Purpose | Required RBAC Role |
|---|---|---|
| App Registration | Workload identity for GitHub OIDC | N/A (this IS the identity) |
| ORC App Service | Deployment target | Contributor |

| GitHub Secret | Azure Source |
|---|---|
| `AZURE_CLIENT_ID` | App Registration > Overview > Application (client) ID |
| `AZURE_TENANT_ID` | App Registration > Overview > Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Subscriptions > Overview > Subscription ID |
| `AZURE_ORC_WEBAPP_NAME` | App Services > Overview > Name |
