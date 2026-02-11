# Azure AD / Entra ID Setup — ORC Repo

The ORC's GitHub Actions workflows authenticate to Azure using **workload identity federation** (OIDC). This eliminates the need to store Azure credentials as GitHub secrets — instead, GitHub Actions presents a short-lived token that Azure trusts based on a pre-configured trust relationship.

This guide walks through creating the Azure AD app registration, configuring federated credentials for GitHub Actions, assigning RBAC roles on the ORC App Service, and storing the resulting IDs as GitHub secrets.

> **Naming conventions used in this guide:**
> Throughout this document, placeholder values are shown in angle brackets. Replace them with your actual values.
>
> | Placeholder | Description | Example |
> |---|---|---|
> | `<your-org>` | Your GitHub organization or account name | `daisinet` |
> | `<your-orc-repo>` | The GitHub repo containing the ORC (this repo) | `daisi-orc-dotnet` |
> | `<your-orc-app-service>` | The Azure App Service name where the ORC is deployed | `daisi-orc` |
> | `<your-app-registration>` | The name of your Azure AD app registration | `daisi-github-deployments` |
> | `<your-resource-group>` | The Azure resource group containing the ORC App Service | `daisi-rg` |

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
    |   (org: <your-org>, repo: <your-orc-repo>, branch: main)
    |
    |-- issues Azure access token for the app registration's service principal
    |
    v
Azure App Service (<your-orc-app-service>)
    |-- service principal has Contributor role
    |-- workflow deploys code and updates app settings
```

---

## Step 1: Create an App Registration

An **app registration** is an identity in Azure AD that your GitHub Actions workflows will authenticate as. If you already have one for other DAISI deployments, you can reuse it — skip to Step 2.

1. Sign in to the [Azure Portal](https://portal.azure.com)
2. Search for **"App registrations"** in the top search bar and select it
3. Click **+ New registration**
4. Fill in the form:
   - **Name**: A descriptive name for this identity (e.g. `<your-app-registration>`)
   - **Supported account types**: **Accounts in this organizational directory only** (single tenant)
   - **Redirect URI**: Leave blank (not needed for workload identity)
5. Click **Register**
6. On the app's **Overview** page, note these two values — you'll need them as GitHub secrets:
   - **Application (client) ID** — a GUID identifying this app registration. This becomes the `AZURE_CLIENT_ID` secret.
   - **Directory (tenant) ID** — a GUID identifying your Azure AD tenant. This becomes the `AZURE_TENANT_ID` secret.

---

## Step 2: Add a Federated Credential for the ORC Repo

A **federated credential** tells Azure AD to trust OIDC tokens from GitHub Actions when they come from a specific repo and branch. This is what allows `azure/login@v2` to work without storing any Azure passwords or certificates.

1. In the Azure Portal, go to **App registrations** > select your app (e.g. `<your-app-registration>`)
2. In the left sidebar, click **Certificates & secrets**
3. Click the **Federated credentials** tab
4. Click **+ Add credential**
5. Select **Federated credential scenario**: **GitHub Actions deploying Azure resources**
6. Fill in the form:
   - **Organization**: `<your-org>` — your GitHub organization or account name (e.g. `daisinet`)
   - **Repository**: `<your-orc-repo>` — the name of the ORC repo, without the org prefix (e.g. `daisi-orc-dotnet`)
   - **Entity type**: **Branch**
   - **GitHub branch name**: `main` — the branch that your deployment workflows run on
   - **Name**: A label for this credential (e.g. `<your-orc-repo>-main`)
   - **Description**: Optional (e.g. `Deploy ORC from main branch`)
   - **Issuer**: Should be pre-filled with `https://token.actions.githubusercontent.com`
   - **Subject identifier**: Should be pre-filled with `repo:<your-org>/<your-orc-repo>:ref:refs/heads/main`
7. Click **Add**

### Why `main` branch?

Both `deploy-orc.yml` and `orchestrate-release.yml` are triggered via `workflow_dispatch`, which runs on the default branch. The federated credential must match the branch the workflow runs on. If you later add tag-triggered workflows, you'll need a separate federated credential for that entity type.

> **Important**: Each combination of (org, repo, entity type, value) needs its own federated credential. You can add up to 20 per app registration.

---

## Step 3: Find Your Subscription ID

The **subscription ID** identifies which Azure subscription contains your resources. All Azure resources (App Services, Storage, CosmosDB) live within a subscription.

1. In the Azure Portal, search for **"Subscriptions"** in the top search bar
2. Select the subscription that contains your ORC App Service
3. On the subscription's **Overview** page, copy the **Subscription ID** — this GUID becomes the `AZURE_SUBSCRIPTION_ID` secret

---

## Step 4: Create a Service Principal and Assign RBAC Roles

The app registration needs permission to deploy to the ORC App Service and update its settings. Azure uses **Role-Based Access Control (RBAC)** to grant these permissions.

### 4a: Verify the service principal exists

When you created the app registration in Step 1, Azure automatically created a corresponding **service principal** (the identity that can actually be assigned roles). Verify it exists:

1. In the Azure Portal, search for **"Enterprise applications"**
2. Search for your app registration name (e.g. `<your-app-registration>`)
3. You should see it listed — click on it to confirm

### 4b: Assign Contributor role on the ORC App Service

The **Contributor** role grants permission to deploy code, update app settings, and restart the App Service.

1. In the Azure Portal, go to **App Services** > select your ORC app (`<your-orc-app-service>`)
2. In the left sidebar, click **Access control (IAM)**
3. Click **+ Add** > **Add role assignment**
4. On the **Role** tab:
   - Search for **Contributor**
   - Select **Contributor** and click **Next**
5. On the **Members** tab:
   - **Assign access to**: **User, group, or service principal**
   - Click **+ Select members**
   - Search for your app registration name (e.g. `<your-app-registration>`)
   - Select it and click **Select**
6. Click **Review + assign** > **Review + assign** again

### 4c: Assign Contributor role on the Resource Group (alternative)

If you prefer a broader scope (e.g. you'll add more App Services later), you can assign the role at the resource group level instead. This gives the service principal Contributor access to **all resources** in the group.

1. Go to **Resource groups** > select the group containing your ORC App Service
2. Follow the same **Access control (IAM)** > **Add role assignment** steps as above

> **Principle of least privilege**: Scoping to the individual App Service is more secure. Only use the resource group scope if multiple services in the group need deployment access.

---

## Step 5: Store Azure IDs as GitHub Secrets

These secrets are read by the `azure/login@v2` action and `azure/webapps-deploy@v3` action in your workflows.

1. Go to your ORC repo on GitHub (`<your-org>/<your-orc-repo>`)
2. Click **Settings** > **Secrets and variables** > **Actions**
3. Add each secret by clicking **New repository secret**:

| Secret Name | What it is | Where to find it | Example value |
|---|---|---|---|
| `AZURE_CLIENT_ID` | The app registration's unique identifier | Azure Portal > App registrations > your app > Overview > **Application (client) ID** | `a1b2c3d4-e5f6-...` |
| `AZURE_TENANT_ID` | Your Azure AD directory's unique identifier | Azure Portal > App registrations > your app > Overview > **Directory (tenant) ID** | `f7e8d9c0-b1a2-...` |
| `AZURE_SUBSCRIPTION_ID` | The subscription containing your Azure resources | Azure Portal > Subscriptions > your subscription > Overview > **Subscription ID** | `1a2b3c4d-5e6f-...` |
| `AZURE_ORC_WEBAPP_NAME` | The name of the ORC's Azure App Service | Azure Portal > App Services > your ORC app > Overview > **Name** field (also the subdomain in `<name>.azurewebsites.net`) | `daisi-orc` |

> **Note**: `AZURE_ORC_WEBAPP_NAME` is the **name** of the App Service, not a GUID. It's the same value that appears in the URL: `https://<your-orc-app-service>.azurewebsites.net`.

---

## Step 6: Verify the Setup

### Test Azure login from GitHub Actions

1. Go to your ORC repo > **Actions** tab
2. Select the **Deploy ORC** workflow
3. Click **Run workflow**, enter a version like `2026.01.01.0000`
4. Watch the **Azure Login** step — it should succeed without errors
5. If it fails, check:
   - The federated credential's subject matches `repo:<your-org>/<your-orc-repo>:ref:refs/heads/main`
   - The `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` secrets are correct
   - The service principal has `Contributor` role on the App Service

### Test full deployment

If the Azure Login step passes, the workflow should:
1. Build and publish the ORC
2. Deploy to the App Service
3. Update the `Daisi:OrcVersion` app setting

Verify in the Azure Portal:
- **App Services** > your ORC app > **Overview**: should show a recent deployment
- **Settings** > **Environment variables** > **App settings**: `Daisi:OrcVersion` should match the version you entered

---

## Troubleshooting

### "AADSTS700024: Client assertion is not within its valid time range"
- The GitHub Actions runner's clock may be skewed, or the workflow ran on a branch/tag that doesn't match the federated credential. Verify the federated credential's entity type and value.

### "AADSTS70021: No matching federated identity record found"
- The federated credential's subject doesn't match the token GitHub presented. Check:
  - **Organization** matches your GitHub org
  - **Repository** matches your ORC repo name (without the org prefix)
  - **Entity type** is `Branch` and value is `main`
  - If using a different trigger (tag, environment, PR), you need a separate federated credential

### "AuthorizationFailed: The client does not have authorization to perform action"
- The service principal doesn't have the right role. Go to the App Service > **Access control (IAM)** > **Role assignments** tab and verify the app registration appears with the `Contributor` role.

### "The subscription is not found" or "SubscriptionNotFound"
- The `AZURE_SUBSCRIPTION_ID` secret is wrong, or the service principal doesn't have any role in that subscription. Double-check the value and add at least a Reader role at the subscription level if needed.

### "WebApp '...' not found"
- The `AZURE_ORC_WEBAPP_NAME` secret doesn't match the actual App Service name. The name is case-sensitive and must match exactly.

---

## Architecture Reference

| Azure Resource | Purpose | Required RBAC Role |
|---|---|---|
| App Registration | Workload identity for GitHub OIDC | N/A (this IS the identity) |
| ORC App Service | Deployment target for the ORC application | Contributor |

| GitHub Secret | What it is | Azure Source |
|---|---|---|
| `AZURE_CLIENT_ID` | App registration identifier | App registrations > Overview > Application (client) ID |
| `AZURE_TENANT_ID` | Azure AD directory identifier | App registrations > Overview > Directory (tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription identifier | Subscriptions > Overview > Subscription ID |
| `AZURE_ORC_WEBAPP_NAME` | App Service name | App Services > Overview > Name |
