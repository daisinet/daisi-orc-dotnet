using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class McpRPC(
        ILogger<McpRPC> logger,
        McpService mcpService) : McpProto.McpProtoBase
    {
        public override async Task<RegisterMcpServerResponse> RegisterServer(
            RegisterMcpServerRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;
            var userId = context.GetUserId() ?? string.Empty;
            var userName = context.GetHttpContext()?.User?.Identity?.Name ?? string.Empty;

            try
            {
                var server = await mcpService.RegisterServerAsync(
                    accountId, userId, userName,
                    request.Name, request.ServerUrl,
                    request.AuthType.ToString(),
                    request.AuthSecret,
                    request.SyncIntervalMinutes,
                    request.TargetRepositoryId);

                return new RegisterMcpServerResponse
                {
                    Success = true,
                    Server = MapToProto(server)
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to register MCP server for account {AccountId}", accountId);
                return new RegisterMcpServerResponse { Success = false, Error = ex.Message };
            }
        }

        public override async Task<ListMcpServersResponse> ListServers(
            ListMcpServersRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;
            var servers = await mcpService.GetServersAsync(accountId);

            var response = new ListMcpServersResponse();
            foreach (var server in servers)
                response.Servers.Add(MapToProto(server));

            return response;
        }

        public override async Task<GetMcpServerResponse> GetServer(
            GetMcpServerRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;
            var server = await mcpService.GetServerAsync(request.ServerId, accountId);

            if (server == null)
                throw new RpcException(new Status(StatusCode.NotFound, "MCP server not found"));

            return new GetMcpServerResponse { Server = MapToProto(server) };
        }

        public override async Task<UpdateMcpServerResponse> UpdateServer(
            UpdateMcpServerRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;

            try
            {
                var server = await mcpService.GetServerAsync(request.ServerId, accountId);
                if (server == null)
                    return new UpdateMcpServerResponse { Success = false, Error = "Server not found" };

                if (request.Name != null) server.Name = request.Name;
                if (request.ServerUrl != null) server.ServerUrl = request.ServerUrl;
                if (request.AuthType != McpAuthType.McpAuthNone) server.AuthType = request.AuthType.ToString();
                if (request.AuthSecret != null) server.AuthSecretEncrypted = request.AuthSecret;
                if (request.SyncIntervalMinutes > 0) server.SyncIntervalMinutes = request.SyncIntervalMinutes;

                if (request.DiscoveredResources.Count > 0)
                {
                    server.DiscoveredResources = request.DiscoveredResources
                        .Select(r => new McpResourceRecord
                        {
                            Uri = r.Uri,
                            Name = r.Name,
                            MimeType = r.MimeType,
                            Description = r.Description,
                            Enabled = r.Enabled
                        }).ToList();
                }

                var updated = await mcpService.UpdateServerAsync(server);

                return new UpdateMcpServerResponse
                {
                    Success = true,
                    Server = MapToProto(updated)
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to update MCP server {ServerId}", request.ServerId);
                return new UpdateMcpServerResponse { Success = false, Error = ex.Message };
            }
        }

        public override async Task<RemoveMcpServerResponse> RemoveServer(
            RemoveMcpServerRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;

            try
            {
                await mcpService.RemoveServerAsync(request.ServerId, accountId);
                return new RemoveMcpServerResponse { Success = true };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove MCP server {ServerId}", request.ServerId);
                return new RemoveMcpServerResponse { Success = false, Error = ex.Message };
            }
        }

        public override async Task<TriggerMcpSyncResponse> TriggerSync(
            TriggerMcpSyncRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;

            try
            {
                var server = await mcpService.GetServerAsync(request.ServerId, accountId);
                if (server == null)
                    return new TriggerMcpSyncResponse { Success = false, Error = "Server not found" };

                // Find an online host for this account to execute the sync
                var host = HostContainer.HostsOnline.Values
                    .FirstOrDefault(h => h.Host.AccountId == accountId);

                if (host == null)
                    return new TriggerMcpSyncResponse { Success = false, Error = "No host online for this account" };

                // Send McpSyncRequest command to the host
                var syncCommand = BuildSyncCommand(server);
                host.OutgoingQueue.Writer.TryWrite(syncCommand);

                await mcpService.UpdateSyncStatusAsync(server.Id, accountId, "SYNCING");

                return new TriggerMcpSyncResponse { Success = true };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to trigger sync for MCP server {ServerId}", request.ServerId);
                return new TriggerMcpSyncResponse { Success = false, Error = ex.Message };
            }
        }

        public override async Task<GetMcpServerStatusResponse> GetServerStatus(
            GetMcpServerStatusRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId() ?? string.Empty;
            var server = await mcpService.GetServerAsync(request.ServerId, accountId);

            if (server == null)
                throw new RpcException(new Status(StatusCode.NotFound, "MCP server not found"));

            var response = new GetMcpServerStatusResponse
            {
                Status = ParseStatus(server.Status),
                ResourcesSynced = server.ResourcesSynced
            };

            if (server.LastError != null)
                response.LastError = server.LastError;
            if (server.DateLastSync.HasValue)
                response.DateLastSync = Timestamp.FromDateTime(
                    DateTime.SpecifyKind(server.DateLastSync.Value, DateTimeKind.Utc));

            return response;
        }

        internal static Command BuildSyncCommand(McpServerRecord server)
        {
            var syncRequest = new McpSyncRequest
            {
                AccountId = server.AccountId,
                ServerId = server.Id,
                ServerUrl = server.ServerUrl,
                AuthType = server.AuthType,
                AuthSecret = server.AuthSecretEncrypted,
                RepositoryId = server.RepositoryId,
                CreatedByUserId = server.CreatedByUserId
            };

            foreach (var resource in server.DiscoveredResources.Where(r => r.Enabled))
            {
                syncRequest.EnabledResources.Add(new McpEnabledResource
                {
                    Uri = resource.Uri,
                    Name = resource.Name,
                    MimeType = resource.MimeType
                });
            }

            return new Command
            {
                Name = nameof(McpSyncRequest),
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(syncRequest)
            };
        }

        private static McpServerInfo MapToProto(McpServerRecord record)
        {
            var info = new McpServerInfo
            {
                Id = record.Id,
                AccountId = record.AccountId,
                Name = record.Name,
                ServerUrl = record.ServerUrl,
                AuthType = ParseAuthType(record.AuthType),
                Status = ParseStatus(record.Status),
                SyncIntervalMinutes = record.SyncIntervalMinutes,
                RepositoryId = record.RepositoryId,
                CreatedByUserId = record.CreatedByUserId,
                CreatedByUserName = record.CreatedByUserName,
                DateCreated = Timestamp.FromDateTime(
                    DateTime.SpecifyKind(record.DateCreated, DateTimeKind.Utc)),
                ResourcesSynced = record.ResourcesSynced
            };

            if (record.LastError != null)
                info.LastError = record.LastError;
            if (record.DateLastSync.HasValue)
                info.DateLastSync = Timestamp.FromDateTime(
                    DateTime.SpecifyKind(record.DateLastSync.Value, DateTimeKind.Utc));

            foreach (var r in record.DiscoveredResources)
            {
                var resource = new McpDiscoveredResource
                {
                    Uri = r.Uri,
                    Name = r.Name,
                    MimeType = r.MimeType,
                    Description = r.Description ?? string.Empty,
                    Enabled = r.Enabled
                };
                if (r.DateLastSync.HasValue)
                    resource.DateLastSync = Timestamp.FromDateTime(
                        DateTime.SpecifyKind(r.DateLastSync.Value, DateTimeKind.Utc));
                info.DiscoveredResources.Add(resource);
            }

            return info;
        }

        private static McpAuthType ParseAuthType(string authType)
        {
            return authType?.ToUpperInvariant() switch
            {
                "MCP_AUTH_BEARER" or "BEARER" => McpAuthType.McpAuthBearer,
                "MCP_AUTH_API_KEY" or "API_KEY" => McpAuthType.McpAuthApiKey,
                _ => McpAuthType.McpAuthNone
            };
        }

        private static McpServerStatus ParseStatus(string status)
        {
            return status?.ToUpperInvariant() switch
            {
                "CONNECTED" => McpServerStatus.McpStatusConnected,
                "ERROR" => McpServerStatus.McpStatusError,
                "SYNCING" => McpServerStatus.McpStatusSyncing,
                "DISABLED" => McpServerStatus.McpStatusDisabled,
                _ => McpServerStatus.McpStatusPending
            };
        }
    }
}
