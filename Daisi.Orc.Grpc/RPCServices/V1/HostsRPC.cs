using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    [Authorize]
    public class HostsRPC(ILogger<HostsRPC> logger, Cosmo cosmo) : HostsProto.HostsProtoBase
    {
        public async override Task<ArchiveHostResponse> Archive(ArchiveHostRequest request, ServerCallContext context)
        {
            ArchiveHostResponse response = new();
            try
            {
                var accountId = context.GetAccountId();

                var host = await cosmo.GetHostAsync(request.HostId);

                if (accountId != host.AccountId)
                    throw new Exception("DAISI: Invalid Host.");

                // Must happen before setting the status to Archived
                // because the Unregister function sets the status to Offline
                await HostContainer.UnregisterHostAsync(host, cosmo, Program.App.Configuration);

                await cosmo.PatchHostStatusAsync(request.HostId, accountId, HostStatus.Archived);

                response.Success = true;
                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                return response;
            }
        }

        public async override Task<GetSecretKeyResponse> GetSecretKey(GetSecretKeyRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId();
            var host = await cosmo.GetHostAsync(accountId, request.HostId);

            if (host == null) throw new Exception("DAISI: Invalid host.");

            // Use stored SecretKeyId for a point read instead of cross-partition query
            if (!string.IsNullOrEmpty(host.SecretKeyId))
            {
                var key = await cosmo.GetKeyAsync(host.SecretKeyId, KeyTypes.Secret);
                if (key != null) return new GetSecretKeyResponse() { SecretKey = key.Key };
            }

            // Fallback for hosts registered before SecretKeyId was stored
            var keys = await cosmo.GetKeysByOwnerIdAsync(request.HostId);
            var secretKey = keys.FirstOrDefault(k => k.Type == KeyTypes.Secret.Name);

            if (secretKey == null) return new GetSecretKeyResponse();

            return new GetSecretKeyResponse() { SecretKey = secretKey.Key };
        }
        public async override Task<UpdateHostResponse> Update(UpdateHostRequest request, ServerCallContext context)

        {
            UpdateHostResponse response = new();
            try
            {
                var accountId = context.GetAccountId();

                var host = await cosmo.GetHostAsync(accountId, request.Host.Id);

                host.Name = request.Host.Name;
                host.DirectConnect = request.Host.DirectConnect;
                host.PeerConnect = request.Host.PeerConnect;
                host.ToolsOnly = request.Host.ToolsOnly;
                host.ReleaseGroup = request.Host.ReleaseGroup ?? host.ReleaseGroup;

                await HostContainer.UpdateConfigurableWebSettingsAsync(host);

                host = await cosmo.PatchHostForWebUpdateAsync(host);

                response.Success = true;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = ex.GetBaseException().Message;
            }
            return response;

        }
        public async override Task<GetHostsResponse> GetHosts(GetHostsRequest request, ServerCallContext context)
        {
            var accountId = context.GetHttpContext().User.GetAccountId();

            var result = new GetHostsResponse() { };
            var hosts = await cosmo.GetHostsAsync(accountId, request.Paging.SearchTerm, request.Paging.HasPageSize ? request.Paging.PageSize : 10, request.Paging.HasPageIndex ? request.Paging.PageIndex : 0);

            result.Hosts.AddRange(hosts.Items.Select(host => new Protos.V1.Host()
            {
                Name = host.Name,
                IpAddress = host.IpAddress,
                Port = host.Port,
                DateStarted = host.DateStarted.HasValue ? Timestamp.FromDateTime(host.DateStarted.Value) : null,
                DateLastHeartbeat = host.DateLastHeartbeat.HasValue ? Timestamp.FromDateTime(host.DateLastHeartbeat.Value) : null,
                DateLastSession = host.DateLastSession.HasValue ? Timestamp.FromDateTime(host.DateLastSession.Value) : null,
                DateStopped = host.DateStopped.HasValue ? Timestamp.FromDateTime(host.DateStopped.Value) : null,
                OperatingSystem = host.OperatingSystem,
                OperatingSystemVersion = host.OperatingSystemVersion,
                AppVersion = host.AppVersion,
                Id = host.Id.ToString(),
                Region = host.Region.ToString(),
                DirectConnect = host.DirectConnect,
                PeerConnect = host.PeerConnect,
                ToolsOnly = host.ToolsOnly,
                Status = host.Status,
                ReleaseGroup = host.ReleaseGroup,
            }));

            result.TotalCount = hosts.TotalCount;

            return result;
        }

        public async override Task<GetHostStatsResponse> GetHostStats(GetHostStatsRequest request, ServerCallContext context)
        {
            var accountId = context.GetAccountId();
            var response = new GetHostStatsResponse();
            var now = DateTime.UtcNow;

            // Verify the host belongs to this account
            var host = await cosmo.GetHostAsync(accountId, request.HostId);
            if (host == null)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Invalid host."));

            // Determine date range based on timeframe
            DateTime? startDate = request.Timeframe switch
            {
                Timeframe.Day => now.Date,
                Timeframe.Week => now.Date.AddDays(-(int)now.DayOfWeek),
                Timeframe.Month => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                Timeframe.Year => new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => null
            };

            var messages = await cosmo.GetInferenceMessageStatsForHostAsync(request.HostId, startDate);

            // Group into time buckets
            Dictionary<int, List<InferenceMessageStat>> grouped;
            int bucketCount;

            switch (request.Timeframe)
            {
                case Timeframe.Day:
                    grouped = messages.GroupBy(m => m.DateCreated.Hour)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    bucketCount = 24;
                    break;
                case Timeframe.Week:
                    grouped = messages.GroupBy(m => (int)(m.DateCreated.Date - startDate!.Value).TotalDays)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    bucketCount = 7;
                    break;
                case Timeframe.Month:
                    grouped = messages.GroupBy(m => m.DateCreated.Day - 1)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    bucketCount = DateTime.DaysInMonth(now.Year, now.Month);
                    break;
                case Timeframe.Year:
                    grouped = messages.GroupBy(m => m.DateCreated.Month - 1)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    bucketCount = 12;
                    break;
                default: // All time - bucket by month
                    if (!messages.Any())
                        return response;
                    var earliest = messages.Min(m => m.DateCreated);
                    startDate = new DateTime(earliest.Year, earliest.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    grouped = messages.GroupBy(m =>
                            (m.DateCreated.Year - earliest.Year) * 12 + m.DateCreated.Month - earliest.Month)
                        .ToDictionary(g => g.Key, g => g.ToList());
                    var monthDiff = (now.Year - earliest.Year) * 12 + now.Month - earliest.Month;
                    bucketCount = monthDiff + 1;
                    break;
            }

            for (int i = 0; i < bucketCount; i++)
            {
                var stat = new HostStat
                {
                    HostId = request.HostId,
                    Timeframe = request.Timeframe,
                    Part = i
                };

                if (grouped.TryGetValue(i, out var bucket))
                {
                    stat.TokenCount = bucket.Sum(m => (long)m.TokenCount);
                    stat.SecondsProcessingTokens = (long)bucket.Sum(m => m.TokenProcessingSeconds);
                }

                DateTime bucketDate = request.Timeframe switch
                {
                    Timeframe.Day => startDate!.Value.AddHours(i),
                    Timeframe.Week => startDate!.Value.AddDays(i),
                    Timeframe.Month => startDate!.Value.AddDays(i),
                    Timeframe.Year => new DateTime(now.Year, i + 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    _ => startDate!.Value.AddMonths(i)
                };
                stat.Date = Timestamp.FromDateTime(DateTime.SpecifyKind(bucketDate, DateTimeKind.Utc));

                response.Stats.Add(stat);
            }

            return response;
        }

        public async override Task<RegisterHostResponse> Register(RegisterHostRequest request, ServerCallContext context)
        {
            RegisterHostResponse response = new();
            var httpContext = context.GetHttpContext();
            var ipAddress = context.GetRemoteIpAddress();
            var clientKey = httpContext.User.GetClientKey();

            Core.Data.Models.Host host = new()
            {
                IpAddress = ipAddress ?? string.Empty,
                Name = request.Host.Name,
                Port = request.Host.Port,
                DateCreated = DateTime.UtcNow,
                AccountId = httpContext.User.GetAccountId(),
                Region = System.Enum.Parse<HostRegions>(request.Host.Region),
                Status = HostStatus.Unknown,
                OperatingSystem = request.Host.OperatingSystem,
                OperatingSystemVersion = request.Host.OperatingSystemVersion,
                DirectConnect = request.Host.DirectConnect,
                PeerConnect = request.Host.PeerConnect,
                ToolsOnly = request.Host.ToolsOnly
            };

            host = await cosmo.CreateHostAsync(host);

            var secretKey = await cosmo.CreateSecretKeyAsync(new AccessKeyOwner()
            {
                AccountId = host.AccountId,
                Id = host.Id,
                Name = request.Host.Name,
                SystemRole = SystemRoles.HostDevice
            });

            // Store the secret key ID on the host for efficient lookups
            host.SecretKeyId = secretKey.Id;
            await cosmo.PatchHostSecretKeyIdAsync(host.Id, host.AccountId, secretKey.Id);

            response.SecretKey = secretKey.Key;
            response.HostId = host.Id;

            return response;
        }
    }
}
