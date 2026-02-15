# Daisi Orchestrator (Orc)
The Orchestration engine for the DAISI system is designed to be the communication hub for the entire system of networks. The service layer uses gRPC for efficient and fast communications over http/2 connections. 
Proto contracts for the service layers are found in the Daisi.SDK project, along with corresponding Clients for the various services.

### Project - Daisi.Orc.Grpc
This is the main console project that can be hosted in the cloud or run from a console application locally during development. Cloud environments should be secured with SSL. Private networks *can lose the overhead of SSL* if the data
isn't leaving the local network, but SSL is still recommended even for private networks.

#### User Secrets

```
{
  "Cosmo": {
    "ConnectionString": "<< CosmoDb Connection String Here >>"
  },
  "Daisi": {
    "AccountId": "<< Daisi Orc Account ID >>", // Every orc is associated with an account in the database, even public Orcs.
    "OrcVersion": "1.0.0", // The version of this Orc instance. Updated automatically by deploy-orc.yml on each release.
    "MaxHosts": 20, // Maximum number of hosts supported by the Orc. Future Hosts that attempt to connect will be sent to "NextOrcId"
    "NextOrcId": "orc-xxx-next", // The ID of the Orc to send overflow. You can daisy-chain (pun intended) Orcs and even have them loop until a connection slot can be found.
    "OrcId": "orc-xxx-this" // The ID of this Orc
  }
}
```

### Inference Receipt Processing

When a host sends an `InferenceReceipt`, the ORC's `InferenceReceiptCommandHandler` processes it in two stages:

1. **Token stats** — The receipt is always persisted via `RecordInferenceMessageAsync` so the host dashboard can display token-processing metrics. This is unconditional — every inference produces stats.
2. **Credit settlement** — The handler resolves the consumer's client key (`receipt.ConsumerClientKey`) to an account via `Cosmo.GetKeyAsync`. If the resolved consumer account differs from the host's owner account, `CreditService.ProcessInferenceReceiptAsync` awards credits to the host and debits the consumer. If the client key is empty or unresolvable (e.g. internal/bot usage, same-account testing), no credit transaction occurs.

This ensures the dashboard always has data while credits only transfer between distinct accounts.

#### Receipt Deduplication

The `InferenceReceiptCommandHandler` maintains an in-memory `ConcurrentDictionary` keyed by `"{hostId}:{inferenceId}"` to prevent the same receipt from being processed twice. If a duplicate receipt arrives, it is logged and skipped. Entries expire after 24 hours via periodic cleanup. This adds zero latency — it's a single dictionary lookup before processing.

### Credit Anomaly Detection

The ORC includes an async anomaly detection system that scans for suspicious credit patterns without adding latency to the inference hot path. All checks run in the `CreditAnomalyService` background service every 30 minutes.

#### Anomaly Types

| Type | Severity | Description |
|------|----------|-------------|
| **Inflated Tokens** | Medium/High | Host's average token count per inference exceeds 10x the network-wide median |
| **Receipt Volume Spike** | Medium/High | Host submits more receipts in the last hour than 3x its 7-day hourly average |
| **Zero Work Uptime** | Low | Host has been online 7+ days with zero inferences processed |
| **Circular Credit Flow** | High | Two accounts consistently serve each other's consumers (potential collusion) |
| **Receipt Replay** | High | Same receipt submitted multiple times (blocked by dedup, logged as anomaly) |

#### Architecture

- **Detection**: `CreditAnomalyService` (background service) queries inference stats and credit transactions to identify anomalous patterns.
- **Storage**: Anomalies are stored in a `CreditAnomalies` CosmosDB container partitioned by `AccountId`.
- **Review**: Admin-only gRPC RPCs (`GetCreditAnomalies`, `ReviewCreditAnomaly`) in `CreditsRPC` allow admins to list, filter, and review anomalies.
- **UI**: The Manager admin panel includes a "Credit Anomalies" tab for reviewing flagged accounts.

#### Design Principles

- **No hot-path changes** — inference latency is unaffected
- **No auto-penalization** — anomalies are flagged for human review, not auto-acted on
- **After-the-fact detection** — all checks run asynchronously in a background service

### Project - Daisi.Orc.Core
This is the core library that contains various interfaces and a CosmoDb repository, which is used by default. Abstraction for other databases and repository types is planned, but not yet implemented.

#### Release Management
The `HostRelease` data model and `Cosmo.Releases.cs` partial class provide database-driven release management. Releases are stored in a `Releases` CosmosDB container partitioned by `ReleaseGroup`. Each release record tracks a timestamp version, a semver (for Velopack), a download URL, activation status, and optional release notes.

The `ReleasesRPC` gRPC service exposes CRUD operations: `Create`, `GetReleases`, `GetActiveRelease`, and `Activate`. Activating a release deactivates all others in the same group, enabling instant rollback by re-activating an older release.

During heartbeat and environment checks, the Orc queries the active release for the host's release group. If the host's version is older than the active release, an `UpdateRequiredRequest` is sent with the appropriate Velopack **channel** and the host self-updates from Azure Blob Storage (`releases/velopack/{channel}/{rid}/`).

**Production as a version floor:** Production releases act as a minimum version for all release groups. When a non-production host checks for updates, the Orc compares the host's group-specific active release against the production active release and picks whichever has the higher version. If production wins, the host receives `Channel=production` so it downloads from the production blob path. This means that when production ships a version newer than what a group has, all hosts across every group will update to at least the production version — no host can fall behind production regardless of its release group.

#### One-Click Release Automation
The ORC hosts the central orchestration workflow and the `TriggerRelease` gRPC endpoint. When a user clicks "Start Release" in the Manager, the ORC dispatches a GitHub Actions pipeline that handles SDK publish (if needed), ORC deploy, and Host release automatically.

See **[ReleaseSetup.md](ReleaseSetup.md)** for the full setup guide, or jump directly to:
- **[GitPATSetup.md](GitPATSetup.md)** — Step-by-step GitHub PAT creation for `SDK_PAT` and `RELEASE_PAT`
- **[AzureADSetup.md](AzureADSetup.md)** — Azure AD app registration, federated credentials, and RBAC roles

#### Coordinated Release Order (manual fallback)
When proto or SDK changes land that affect ORC and Host, follow this order:
1. Tag `daisi-sdk-dotnet` with `v*` (e.g. `v1.0.10`) — NuGet package publishes automatically to nuget.org
2. Deploy ORC — includes SDK via ProjectReference, so it picks up changes immediately
3. Tag `daisi-hosts-dotnet` with `beta-*` — workflow builds the host, uploads blob, writes release record
4. Phased host rollout: beta → group1 → group2 → production


### Project - Daisi.Functions.CosmoDb
This project should be hosted with access to the CosmoDb so that it can track CRUD operations on the db and keep references up to date as changes are made.

#### User Secrets
```
{
  "Cosmo": {
    "ConnectionString": "<< Cosmo DB Connection String Here >>"
  }
}
```
