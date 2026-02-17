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

### Tool Delegation Routing

The ORC supports **tool delegation** for tools-only hosts. When a host is marked as `ToolsOnly`, it is excluded from inference session routing but remains available to execute tools on behalf of other hosts.

**How it works:**
1. An inference host sends an `ExecuteToolRequest` command to the ORC via the command channel.
2. The ORC's `ToolExecutionCommandHandler` finds an available tools-only host in the same account using `GetNextToolsOnlyHost()` (LRU ordering).
3. The ORC forwards the request to the tools-only host via `SendToolExecutionToHostAsync()`.
4. The tools-only host executes the tool and returns an `ExecuteToolResponse`.
5. The ORC relays the response back to the requesting inference host.

The `Host` data model includes a `ToolsOnly` boolean field that is:
- Persisted in Cosmos DB (included in `PatchHostForWebUpdateAsync`)
- Synced to in-memory state via `UpdateConfigurableWebSettingsAsync`
- Propagated through `HostsRPC.Register`, `Update`, and `GetHosts`
- Filterable in `GetNextHost()` (tools-only hosts are excluded) and `GetNextToolsOnlyHost()` (only tools-only hosts are returned)

### Secure Tool Lifecycle (Install/Uninstall + Discovery)

The ORC manages the secure tool lifecycle — install/uninstall notifications and tool discovery — but is **not** in the execution hot path. Consumer hosts and the Manager UI call providers directly via HTTP.

**Key components:**
- `SecureToolService` — Handles `NotifyProviderInstallAsync` (HTTP POST to provider `/install` on purchase), `NotifyProviderUninstallAsync` (HTTP POST to provider `/uninstall` on deactivation), and `GetInstalledToolsAsync` (queries purchases and returns tool definitions with `InstallId`, `EndpointUrl`, and `BundleInstallId` for direct provider communication).
- `SecureToolRPC` — gRPC service implementing `SecureToolProto.SecureToolProtoBase` with `GetInstalledSecureTools` only. Returns `InstallId`, `EndpointUrl`, and `BundleInstallId` per tool.
- `MarketplacePurchase.SecureInstallId` — Opaque identifier generated on purchase, shared with the provider. Never contains AccountId.
- `MarketplacePurchase.BundleInstallId` — Shared bundle identifier for OAuth. All tools in a plugin bundle share this ID so users only need to OAuth-connect once per provider.
- `MarketplaceItem` extensions — Fields: `IsSecureExecution`, `SecureEndpointUrl`, `SecureAuthKey`, `SetupParameters`, `SecureToolDefinition`.
- `SetupParameterData` includes `AuthUrl` and `ServiceLabel` fields for OAuth-type parameters. These are pure pass-through — the ORC stores and returns them but does not interpret or act on them. The OAuth lifecycle is entirely between the provider and the Manager UI.

**Architecture:**
- On purchase, the ORC generates an `InstallId` (via `Cosmo.GenerateId("inst")`) and HTTP POSTs to the provider's `/install` endpoint with `X-Daisi-Auth`.
- **Bundle purchase flow:** When a Plugin with `BundledItemIds` is purchased, the ORC generates a shared `BundleInstallId` (via `Cosmo.GenerateId("binst")`), creates child purchase records for each bundled secure tool (each with its own `InstallId` but sharing the `BundleInstallId`), and notifies the provider for each tool with both IDs.
- On deactivation (subscription expiry, cancellation), the ORC HTTP POSTs to `/uninstall`.
- Hosts and Manager call `/execute` and `/configure` directly using the `InstallId` — no ORC relay. OAuth calls use `BundleInstallId` when present.
- For free approved tools, a deterministic `InstallId` is generated from a SHA-256 hash of `accountId:itemId` so AccountId is never exposed.

**Provider API contract:** Providers implement four HTTP POST endpoints (`/install`, `/uninstall`, `/configure`, `/execute`) documented in the [Secure Tool API Reference](https://daisi.ai/learn/marketplace/secure-tool-api-reference).

### Per-Model Backend Engine & Inference Parameters

The ORC stores per-model configuration in CosmosDB and serves it to hosts and the Manager UI via gRPC.

**Backend Engine:** Each model can specify a `BackendEngine` in its `BackendSettings` (e.g. `"LlamaSharp"`, `"OnnxRuntimeGenAI"`). This tells the host which inference backend to use when loading the model. The Manager admin dialog auto-sets this based on the selected file type (GGUF → LlamaSharp, ONNX → OnnxRuntimeGenAI).

**Inference Parameter Defaults:** Models can override default inference parameters (`Temperature`, `TopP`, `TopK`, `RepeatPenalty`, `PresencePenalty`) at the model level. These are stored as optional fields in `BackendSettings` and applied by the host when the request doesn't explicitly set them:
- Per-request values (highest priority)
- Per-model defaults (from `BackendSettings`)
- Hardcoded defaults in `TextGenerationParams` (lowest priority)

**Multi-Type Support:** Models support multiple types via the `Types` repeated field (e.g. a vision-language model can be both `TextGeneration` and `ImageGeneration`). The `Type` singular field is maintained for backward compatibility with older hosts and is set to the first type in `Types`.

**HuggingFace ONNX Detection:** The `HuggingFaceService` now queries the HuggingFace API with `expand[]=onnx` and parses `.onnx` files from the siblings list, returning them in a separate `ONNXFiles` collection alongside GGUF files.

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
