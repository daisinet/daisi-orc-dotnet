using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.HostCommandHandlers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;
using DbHost = Daisi.Orc.Core.Data.Models.Host;

namespace Daisi.Orc.Tests.CommandServices
{
    /// <summary>
    /// Unit tests for tools-only host features: GetNextToolsOnlyHost filtering,
    /// UpdateConfigurableWebSettings ToolsOnly sync, SendToolExecutionToHostAsync,
    /// and ToolExecutionCommandHandler routing.
    /// </summary>
    public class ToolsOnlyHostTests
    {
        private readonly ILogger _logger = NullLogger.Instance;

        [Fact]
        public void GetNextToolsOnlyHost_ReturnsToolsOnlyHost()
        {
            string hostId = $"toh-{Guid.NewGuid():N}";
            string accountId = $"acct-{Guid.NewGuid():N}";
            var host = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId,
                    Name = "ToolsHost",
                    AccountId = accountId,
                    ToolsOnly = true,
                    Status = HostStatus.Online
                }
            };
            HostContainer.HostsOnline.TryAdd(hostId, host);

            try
            {
                var result = HostContainer.GetNextToolsOnlyHost(accountId);

                Assert.NotNull(result);
                Assert.Equal(hostId, result.Host.Id);
                Assert.True(result.Host.ToolsOnly);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId, out _);
            }
        }

        [Fact]
        public void GetNextToolsOnlyHost_ReturnsNull_WhenNoToolsOnlyHosts()
        {
            string hostId = $"toh-{Guid.NewGuid():N}";
            string accountId = $"acct-{Guid.NewGuid():N}";
            var host = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId,
                    Name = "RegularHost",
                    AccountId = accountId,
                    ToolsOnly = false,
                    Status = HostStatus.Online
                }
            };
            HostContainer.HostsOnline.TryAdd(hostId, host);

            try
            {
                var result = HostContainer.GetNextToolsOnlyHost(accountId);

                Assert.Null(result);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId, out _);
            }
        }

        [Fact]
        public void GetNextToolsOnlyHost_FiltersByAccountId()
        {
            string sameAccountId = $"acct-{Guid.NewGuid():N}";
            string otherAccountId = $"acct-{Guid.NewGuid():N}";

            string hostId1 = $"toh-{Guid.NewGuid():N}";
            string hostId2 = $"toh-{Guid.NewGuid():N}";

            var sameAccountHost = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId1,
                    Name = "SameAccountToolsHost",
                    AccountId = sameAccountId,
                    ToolsOnly = true,
                    Status = HostStatus.Online
                }
            };
            var otherAccountHost = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId2,
                    Name = "OtherAccountToolsHost",
                    AccountId = otherAccountId,
                    ToolsOnly = true,
                    Status = HostStatus.Online
                }
            };

            HostContainer.HostsOnline.TryAdd(hostId1, sameAccountHost);
            HostContainer.HostsOnline.TryAdd(hostId2, otherAccountHost);

            try
            {
                var result = HostContainer.GetNextToolsOnlyHost(sameAccountId);

                Assert.NotNull(result);
                Assert.Equal(hostId1, result.Host.Id);
                Assert.Equal(sameAccountId, result.Host.AccountId);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId1, out _);
                HostContainer.HostsOnline.TryRemove(hostId2, out _);
            }
        }

        [Fact]
        public void GetNextToolsOnlyHost_ReturnsLeastRecentlyUsed()
        {
            string accountId = $"acct-{Guid.NewGuid():N}";
            string hostId1 = $"toh-{Guid.NewGuid():N}";
            string hostId2 = $"toh-{Guid.NewGuid():N}";

            var recentHost = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId1,
                    Name = "RecentHost",
                    AccountId = accountId,
                    ToolsOnly = true,
                    Status = HostStatus.Online,
                    DateLastSession = DateTime.UtcNow
                }
            };
            var olderHost = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId2,
                    Name = "OlderHost",
                    AccountId = accountId,
                    ToolsOnly = true,
                    Status = HostStatus.Online,
                    DateLastSession = DateTime.UtcNow.AddMinutes(-30)
                }
            };

            HostContainer.HostsOnline.TryAdd(hostId1, recentHost);
            HostContainer.HostsOnline.TryAdd(hostId2, olderHost);

            try
            {
                var result = HostContainer.GetNextToolsOnlyHost(accountId);

                Assert.NotNull(result);
                Assert.Equal(hostId2, result.Host.Id);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId1, out _);
                HostContainer.HostsOnline.TryRemove(hostId2, out _);
            }
        }

        [Fact]
        public void GetNextToolsOnlyHost_ExcludesOfflineHosts()
        {
            string accountId = $"acct-{Guid.NewGuid():N}";
            string hostId = $"toh-{Guid.NewGuid():N}";

            var offlineHost = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId,
                    Name = "OfflineToolsHost",
                    AccountId = accountId,
                    ToolsOnly = true,
                    Status = HostStatus.Offline
                }
            };
            HostContainer.HostsOnline.TryAdd(hostId, offlineHost);

            try
            {
                var result = HostContainer.GetNextToolsOnlyHost(accountId);

                Assert.Null(result);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId, out _);
            }
        }

        [Fact]
        public async Task UpdateConfigurableWebSettings_SyncsToolsOnlyFlag()
        {
            string hostId = $"toh-{Guid.NewGuid():N}";
            var host = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId,
                    Name = "TestHost",
                    ToolsOnly = false
                }
            };
            HostContainer.HostsOnline.TryAdd(hostId, host);

            try
            {
                Assert.False(HostContainer.HostsOnline[hostId].Host.ToolsOnly);

                var updatedHost = new DbHost
                {
                    Id = hostId,
                    Name = "TestHost",
                    ToolsOnly = true,
                    DirectConnect = false,
                    PeerConnect = false
                };

                await HostContainer.UpdateConfigurableWebSettingsAsync(updatedHost);

                Assert.True(HostContainer.HostsOnline[hostId].Host.ToolsOnly);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId, out _);
            }
        }

        [Fact]
        public async Task SendToolExecutionToHost_WritesToOutgoingQueue()
        {
            string hostId = $"toh-{Guid.NewGuid():N}";
            var host = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = hostId,
                    Name = "ToolsHost",
                    ToolsOnly = true,
                    Status = HostStatus.Online
                }
            };
            HostContainer.HostsOnline.TryAdd(hostId, host);

            try
            {
                var request = new ExecuteToolRequest
                {
                    ToolId = "test-tool",
                    RequestingHostId = "requesting-host"
                };

                // Start the SendToolExecution in background (it will wait for response)
                var sendTask = Task.Run(async () =>
                    await HostContainer.SendToolExecutionToHostAsync(hostId, request, millisecondsToWait: 3000));

                // Read the command from the outgoing queue
                using var cts = new CancellationTokenSource(2000);
                var outgoingCommand = await host.OutgoingQueue.Reader.ReadAsync(cts.Token);

                Assert.Equal(nameof(ExecuteToolRequest), outgoingCommand.Name);
                Assert.NotEmpty(outgoingCommand.RequestId);
                Assert.True(outgoingCommand.Payload.TryUnpack<ExecuteToolRequest>(out var sentRequest));
                Assert.Equal("test-tool", sentRequest.ToolId);

                // Simulate the tools-only host responding
                var responseCommand = new Command
                {
                    Name = nameof(ExecuteToolResponse),
                    RequestId = outgoingCommand.RequestId,
                    Payload = Any.Pack(new ExecuteToolResponse
                    {
                        Success = true,
                        Output = "tool output"
                    })
                };

                // Write response to the per-request channel
                Assert.True(host.RequestChannels.TryGetValue(outgoingCommand.RequestId, out var requestChannel));
                requestChannel.Writer.TryWrite(responseCommand);

                var result = await sendTask;
                Assert.NotNull(result);
                Assert.True(result.Success);
                Assert.Equal("tool output", result.Output);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(hostId, out _);
            }
        }

        [Fact]
        public async Task SendToolExecutionToHost_ReturnsNull_WhenHostNotOnline()
        {
            var result = await HostContainer.SendToolExecutionToHostAsync("nonexistent-host", new ExecuteToolRequest());

            Assert.Null(result);
        }

        [Fact]
        public async Task ToolExecutionCommandHandler_SendsError_WhenRequestingHostNotFound()
        {
            var handler = new ToolExecutionCommandHandler(
                NullLogger<ToolExecutionCommandHandler>.Instance);

            var responseChannel = Channel.CreateUnbounded<Command>();
            var command = new Command
            {
                Name = nameof(ExecuteToolRequest),
                RequestId = "req-1",
                Payload = Any.Pack(new ExecuteToolRequest
                {
                    ToolId = "test-tool",
                    RequestingHostId = "nonexistent-host"
                })
            };

            await handler.HandleAsync("nonexistent-host", command, responseChannel.Writer);

            Assert.True(responseChannel.Reader.TryRead(out var response));
            Assert.Equal(nameof(ExecuteToolResponse), response.Name);
            Assert.True(response.Payload.TryUnpack<ExecuteToolResponse>(out var toolResponse));
            Assert.False(toolResponse.Success);
            Assert.Contains("not found", toolResponse.ErrorMessage);
        }

        [Fact]
        public async Task ToolExecutionCommandHandler_SendsError_WhenNoToolsOnlyHostAvailable()
        {
            string inferenceHostId = $"toh-{Guid.NewGuid():N}";
            string accountId = $"acct-{Guid.NewGuid():N}";

            var inferenceHost = new HostOnline(_logger)
            {
                Host = new DbHost
                {
                    Id = inferenceHostId,
                    Name = "InferenceHost",
                    AccountId = accountId,
                    ToolsOnly = false,
                    Status = HostStatus.Online
                }
            };
            HostContainer.HostsOnline.TryAdd(inferenceHostId, inferenceHost);

            try
            {
                var handler = new ToolExecutionCommandHandler(
                    NullLogger<ToolExecutionCommandHandler>.Instance);

                var responseChannel = Channel.CreateUnbounded<Command>();
                var command = new Command
                {
                    Name = nameof(ExecuteToolRequest),
                    RequestId = "req-2",
                    Payload = Any.Pack(new ExecuteToolRequest
                    {
                        ToolId = "test-tool",
                        RequestingHostId = inferenceHostId
                    })
                };

                await handler.HandleAsync(inferenceHostId, command, responseChannel.Writer);

                Assert.True(responseChannel.Reader.TryRead(out var response));
                Assert.Equal(nameof(ExecuteToolResponse), response.Name);
                Assert.True(response.Payload.TryUnpack<ExecuteToolResponse>(out var toolResponse));
                Assert.False(toolResponse.Success);
                Assert.Contains("No tools-only host", toolResponse.ErrorMessage);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove(inferenceHostId, out _);
            }
        }

        [Fact]
        public async Task ToolExecutionCommandHandler_IgnoresNonMatchingCommandName()
        {
            var handler = new ToolExecutionCommandHandler(
                NullLogger<ToolExecutionCommandHandler>.Instance);

            var responseChannel = Channel.CreateUnbounded<Command>();
            var command = new Command
            {
                Name = "SomeOtherCommand",
                RequestId = "req-3"
            };

            await handler.HandleAsync("any-host", command, responseChannel.Writer);

            // No response should be written for non-matching command names
            Assert.False(responseChannel.Reader.TryRead(out _));
        }
    }
}
