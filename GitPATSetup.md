# GitHub Personal Access Token Setup — daisi-orc-dotnet

The ORC repo requires two GitHub PATs: one for checking out dependency repos during CI builds (`SDK_PAT`), and one for dispatching workflows across repos and calling the GitHub API at runtime (`RELEASE_PAT`).

---

## PAT 1: `SDK_PAT` — Dependency Checkout

This token allows GitHub Actions workflows to check out the `daisi-sdk-dotnet` repo (a private dependency) during the ORC build.

### Step 1: Create the token

1. Sign in to GitHub as a user who has **read access** to `daisinet/daisi-sdk-dotnet`
2. Click your **profile avatar** (top-right) > **Settings**
3. In the left sidebar, scroll down and click **Developer settings**
4. Click **Personal access tokens** > **Fine-grained tokens**
5. Click **Generate new token**
6. Fill in the form:
   - **Token name**: `daisi-orc-sdk-checkout`
   - **Expiration**: 90 days (or your org's maximum; set a calendar reminder to rotate)
   - **Resource owner**: Select `daisinet` (the organization)
   - **Repository access**: Click **Only select repositories**, then select:
     - `daisinet/daisi-sdk-dotnet`
   - **Permissions** > **Repository permissions**:
     - **Contents**: **Read-only** (this is the only permission needed)
     - Leave all other permissions at "No access"
7. Click **Generate token**
8. **Copy the token immediately** — you won't be able to see it again

> **Why not a classic token?** Fine-grained tokens follow the principle of least privilege — this token can only read one repo. A classic token with `repo` scope would grant full read/write to every repo the user has access to.

### Step 2: Add as a GitHub Actions secret

1. Go to [github.com/daisinet/daisi-orc-dotnet](https://github.com/daisinet/daisi-orc-dotnet)
2. Click **Settings** (the repo settings, not your profile settings)
3. In the left sidebar, click **Secrets and variables** > **Actions**
4. Click **New repository secret**
5. Fill in:
   - **Name**: `SDK_PAT`
   - **Secret**: Paste the token from Step 1
6. Click **Add secret**

### What uses this token

| Workflow | Step | Purpose |
|----------|------|---------|
| `deploy-orc.yml` | Checkout daisi-sdk-dotnet | ORC project references `Daisi.SDK.Web` which lives in the SDK repo |

---

## PAT 2: `RELEASE_PAT` — Cross-Repo Workflow Dispatch

This token is used in two places:
1. **GitHub Actions**: The `orchestrate-release.yml` workflow uses it to dispatch workflows in `daisi-sdk-dotnet`, `daisi-orc-dotnet`, and `daisi-hosts-dotnet`, and to poll their run status
2. **ORC Application**: The `GitHubReleaseService` uses it at runtime to trigger `orchestrate-release.yml` via the GitHub REST API

Because it needs write access to Actions across multiple repos, this token requires broader permissions.

### Step 1: Create the token

1. Sign in to GitHub as a user who has **admin or write access** to all three repos
2. Click your **profile avatar** (top-right) > **Settings**
3. In the left sidebar, scroll down and click **Developer settings**
4. Click **Personal access tokens** > **Fine-grained tokens**
5. Click **Generate new token**
6. Fill in the form:
   - **Token name**: `daisi-release-automation`
   - **Expiration**: 90 days (or your org's maximum; set a calendar reminder to rotate)
   - **Resource owner**: Select `daisinet`
   - **Repository access**: Click **Only select repositories**, then select all three:
     - `daisinet/daisi-orc-dotnet`
     - `daisinet/daisi-sdk-dotnet`
     - `daisinet/daisi-hosts-dotnet`
   - **Permissions** > **Repository permissions**:
     - **Actions**: **Read and write** — required to trigger `workflow_dispatch` events and read workflow run status
     - **Contents**: **Read-only** — required for `actions/checkout` and `git diff` in the SDK change detection step
     - Leave all other permissions at "No access"
7. Click **Generate token**
8. **Copy the token immediately**

> **If fine-grained tokens are not available** (e.g. your org hasn't enabled them), create a **classic token** instead:
> 1. Go to **Personal access tokens** > **Tokens (classic)**
> 2. Click **Generate new token (classic)**
> 3. Select scopes: `repo` (full control) and `workflow` (update GitHub Actions workflows)
> 4. Classic tokens cannot be scoped to specific repos — they apply to all repos the user can access

### Step 2: Add as a GitHub Actions secret

1. Go to [github.com/daisinet/daisi-orc-dotnet](https://github.com/daisinet/daisi-orc-dotnet) > **Settings**
2. Click **Secrets and variables** > **Actions**
3. Click **New repository secret**
4. Fill in:
   - **Name**: `RELEASE_PAT`
   - **Secret**: Paste the token from Step 1
5. Click **Add secret**

### Step 3: Add as an ORC application setting

The same token value is also needed by the ORC application at runtime (the `GitHubReleaseService` reads it from configuration to call the GitHub API).

#### For local development

1. In Visual Studio, right-click the **Daisi.Orc.Grpc** project > **Manage User Secrets**
2. Add or merge into the JSON:
   ```json
   {
     "GitHub": {
       "ReleasePAT": "<paste the token here>",
       "OrgName": "daisinet"
     }
   }
   ```
3. Save the file

#### For production (Azure App Service)

1. Go to the [Azure Portal](https://portal.azure.com)
2. Navigate to **App Services** > select your ORC app (e.g. `daisi-orc`)
3. In the left sidebar, click **Settings** > **Environment variables**
4. Under the **App settings** tab, click **+ Add**
5. Add the first setting:
   - **Name**: `GitHub__ReleasePAT` (note: double underscore replaces the colon for environment variables)
   - **Value**: Paste the token
6. Click **Apply**, then add the second:
   - **Name**: `GitHub__OrgName`
   - **Value**: `daisinet`
7. Click **Apply** again, then **Confirm** to restart the app

> **Note**: Azure App Service supports both `GitHub:ReleasePAT` (in the Configuration UI's "Application settings") and `GitHub__ReleasePAT` (as an environment variable). Either works — the colon format is shown in the portal's legacy Configuration blade, and the double-underscore format is used in the newer Environment variables blade.

### What uses this token

| Consumer | How it's accessed | Purpose |
|----------|-------------------|---------|
| `orchestrate-release.yml` | `${{ secrets.RELEASE_PAT }}` via `GH_TOKEN` env var | Dispatch `release-sdk.yml`, `deploy-orc.yml`, `release-host.yml`; poll run status |
| `GitHubReleaseService.cs` | `IConfiguration["GitHub:ReleasePAT"]` | POST to GitHub REST API to trigger `orchestrate-release.yml` |

---

## Token Rotation

Both tokens expire based on the lifetime you set. To rotate:

1. Generate a new token following the same steps above
2. Update the GitHub Actions secret(s) with the new value
3. For `RELEASE_PAT`: also update the Azure App Service app setting and/or user secrets
4. The old token is revoked automatically when it expires, or you can revoke it immediately at **Settings > Developer settings > Personal access tokens**

---

## Troubleshooting

**"Resource not accessible by integration"**
- The token doesn't have access to the target repository. Go to **Settings > Developer settings > Personal access tokens**, edit the token, and verify the repository list includes all required repos.

**"Bad credentials" (401)**
- The token has expired or been revoked. Generate a new one and update all locations where it's stored.

**"Not Found" (404) when dispatching a workflow**
- The token has **Actions** permission but the workflow file doesn't exist on the default branch, OR the token doesn't have access to the repo. Verify both.

**"RequestError: You have exceeded a secondary rate limit"**
- The orchestration workflow polls every 30 seconds. If you run multiple releases in quick succession, you may hit GitHub's secondary rate limits. Wait a few minutes and retry.

---

## Quick Reference

| Secret | Where it's stored | Repos it accesses | Permissions |
|--------|-------------------|-------------------|-------------|
| `SDK_PAT` | GitHub Actions secret | `daisi-sdk-dotnet` | Contents: Read |
| `RELEASE_PAT` | GitHub Actions secret + Azure App Setting | `daisi-orc-dotnet`, `daisi-sdk-dotnet`, `daisi-hosts-dotnet` | Actions: Read/Write, Contents: Read |
