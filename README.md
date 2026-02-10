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
    "MinimumHostVersion": "2025.11.30.00", // Legacy fallback: minimum Host version if no database release exists for the group. Follows dating method YYYY.MM.dd.mm.
    "MinimumHostVersion-beta": "2025.11.30.00", // Legacy fallback for a specific release group (e.g. "beta"). Only used when no active release record is found in the Releases container.
    "OrcVersion": "1.0.0", // The version of this Orc. Used for tandem release safety — host releases with a RequiredOrcVersion will be held back until the Orc meets the minimum.
    "MaxHosts": 20, // Maximum number of hosts supported by the Orc. Future Hosts that attempt to connect will be sent to "NextOrcId"
    "NextOrcId": "orc-xxx-next", // The ID of the Orc to send overflow. You can daisy-chain (pun intended) Orcs and even have them loop until a connection slot can be found.
    "OrcId": "orc-xxx-this" // The ID of this Orc
  }
}
```

### Project - Daisi.Orc.Core
This is the core library that contains various interfaces and a CosmoDb repository, which is used by default. Abstraction for other databases and repository types is planned, but not yet implemented.

#### Release Management
The `HostRelease` data model and `Cosmo.Releases.cs` partial class provide database-driven release management. Releases are stored in a `Releases` CosmosDB container partitioned by `ReleaseGroup`. Each release record tracks a version, download URL, activation status, optional release notes, and an optional `RequiredOrcVersion` for tandem release safety.

The `ReleasesRPC` gRPC service exposes CRUD operations: `Create`, `GetReleases`, `GetActiveRelease`, and `Activate`. Activating a release deactivates all others in the same group, enabling instant rollback by re-activating an older release.

During heartbeat and environment checks, the Orc queries the active release for the host's release group. If the host's version is older than the active release, an `UpdateRequiredRequest` is sent with the versioned blob URL. If no database release exists for the group, the system falls back to the `Daisi:MinimumHostVersion` config key for backward compatibility.


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
