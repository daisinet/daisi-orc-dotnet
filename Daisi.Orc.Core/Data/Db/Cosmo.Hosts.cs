using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Db
{
    public partial class Cosmo
    {
        public const string HostIdPrefex = "host";
        public const string HostsContainerName = "Hosts";
        public const string HostPartitialKeyName = nameof(Host.AccountId);
        public PartitionKey GetPartitionKey(Host host)
        {
            return new(host.AccountId.ToString());
        }


        #region Hosts
        public async Task<Host> CreateHostAsync(Host host)
        {
            host.DateCreated = DateTime.UtcNow;

            var container = await GetContainerAsync(HostsContainerName);
            var item = await container.CreateItemAsync<Host>(host, GetPartitionKey(host));
            return item.Resource;
        }
        public virtual async Task<Host?> GetHostAsync(string hostId)
        {
            var container = await GetContainerAsync(HostsContainerName);
            try
            {
                var item = container.GetItemLinqQueryable<Host>().Where(h => h.Id == hostId && h.Status != Protos.V1.HostStatus.Archived).FirstOrDefault();
                return item;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }
        public virtual async Task<Host?> GetHostAsync(string accountId, string hostId)
        {
            var container = await GetContainerAsync(HostsContainerName);
            try
            {
                var item = await container.ReadItemAsync<Host>(hostId, new PartitionKey(accountId.ToString()));

                if (item?.Resource?.Status == Protos.V1.HostStatus.Archived)
                    return null;

                return item?.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

        }

        public async Task<PagedResult<Host>> GetHostsAsync(string accountId, string? searchTerm = default, int? pageSize = 10, int? pageIndex = 0)
        {
            var container = await GetContainerAsync(HostsContainerName);
            var query = container.GetItemLinqQueryable<Host>()
                .Where(host => host.AccountId == accountId && host.Status != Protos.V1.HostStatus.Archived);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(host => host.Name.ToLower().Contains(searchTerm.ToLower()));
            }


            var results = await query.OrderBy(host => host.Name).ToPagedResultAsync(pageSize, pageIndex);
            return results;
        }
        /// <summary>
        /// Gets the count of non-archived hosts for an account.
        /// </summary>
        public async Task<int> GetHostCountAsync(string accountId)
        {
            var container = await GetContainerAsync(HostsContainerName);
            var count = await container.GetItemLinqQueryable<Host>()
                .Where(h => h.AccountId == accountId && h.Status != Protos.V1.HostStatus.Archived)
                .CountAsync();
            return count;
        }

        #endregion

        #region Patch Host

        /// <summary>
        /// Updates the DateStarted, DateStopped, Status, and IpAddress fields without 
        /// overwriting other fields on the host record
        /// </summary>
        /// <param name="host">The Host to update.</param>
        /// <returns>The Host that was updated.</returns>
        public virtual async Task<Host> PatchHostForConnectionAsync(Host host)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/DateStarted", host.DateStarted),
                PatchOperation.Set("/DateStopped", host.DateStopped),
                PatchOperation.Set("/Status", host.Status),
                PatchOperation.Set("/IpAddress", host.IpAddress),
                PatchOperation.Set("/ConnectedOrc", host.ConnectedOrc)
            };


            var container = await GetContainerAsync(HostsContainerName);
            var response = await container.PatchItemAsync<Host>(host.Id, GetPartitionKey(host), patchOperations);
            return response.Resource;
        }

        /// <summary>
        /// Updates the Name, DirectConnect, and PeerConnect fields without 
        /// overwriting other fields on the host record
        /// </summary>
        /// <param name="host">The Host to update.</param>
        /// <returns>The Host that was updated.</returns>
        public async Task<Host> PatchHostForWebUpdateAsync(Host host)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/Name", host.Name),
                PatchOperation.Set("/DirectConnect", host.DirectConnect),
                PatchOperation.Set("/PeerConnect", host.PeerConnect),
                PatchOperation.Set("/ReleaseGroup", host.ReleaseGroup),
                PatchOperation.Set("/UpdateOperation", "Web"),
            };

            var container = await GetContainerAsync(HostsContainerName);
            var response = await container.PatchItemAsync<Host>(host.Id, GetPartitionKey(host), patchOperations);
            return response.Resource;
        }
        public async Task<Host> PatchHostForUpdateServiceAsync(Host host)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Remove("/UpdateOperation"),
            };

            var container = await GetContainerAsync(HostsContainerName);
            var response = await container.PatchItemAsync<Host>(host.Id, GetPartitionKey(host), patchOperations);
            return response.Resource;
        }
        /// <summary>
        /// Updates the DateLastHeartbeat and PeerConnect fields without 
        /// overwriting other fields on the host record
        /// </summary>
        /// <param name="host">The Host to update.</param>
        /// <returns>The Host that was updated.</returns>
        public async Task<Host> PatchHostForHeartbeatAsync(Host host)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/DateLastHeartbeat", host.DateLastHeartbeat),
                PatchOperation.Set("/Status", host.Status),
                PatchOperation.Set("/ConnectedOrc", host.ConnectedOrc)
            };


            var container = await GetContainerAsync(HostsContainerName);
            var response = await container.PatchItemAsync<Models.Host>(host.Id, GetPartitionKey(host), patchOperations);
            return response.Resource;
        }
        /// <summary>
        /// Updates the OperatingSystem, OperatingSystemVersion, and AppVersion fields without 
        /// overwriting other fields on the host record
        /// </summary>
        /// <param name="host">The Host to update.</param>
        /// <returns>The Host that was updated.</returns>
        public virtual async Task PatchHostEnvironmentAsync(Host host)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/OperatingSystem", host.OperatingSystem),
                PatchOperation.Set("/OperatingSystemVersion", host.OperatingSystemVersion),
                PatchOperation.Set("/AppVersion", host.AppVersion),
            };

            var container = await GetContainerAsync(HostsContainerName);
            var response = await container.PatchItemAsync<Host>(host.Id, GetPartitionKey(host), patchOperations);
        }

        public async Task PatchHostStatusAsync(string hostId, string accountId, Protos.V1.HostStatus status)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/Status", status)
            };

            var container = await GetContainerAsync(HostsContainerName);
            var response = await container.PatchItemAsync<Host>(hostId, new PartitionKey(accountId), patchOperations);
        }

        public async Task PatchHostSecretKeyIdAsync(string hostId, string accountId, string secretKeyId)
        {
            List<PatchOperation> patchOperations = new List<PatchOperation>()
            {
                PatchOperation.Set("/SecretKeyId", secretKeyId)
            };

            var container = await GetContainerAsync(HostsContainerName);
            await container.PatchItemAsync<Host>(hostId, new PartitionKey(accountId), patchOperations);
        }

        #endregion

    }
}
