using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace Daisi.Orc.Tests.CommandServices
{
    /// <summary>
    /// Tests for per-request channel routing in SessionIncomingQueueHandler
    /// and HostOnline.RequestChannels lifecycle. These tests cover the fix
    /// for the destructive queue read bug where concurrent requests on the
    /// same session lost each other's messages.
    /// </summary>
    public class PerRequestChannelTests : IDisposable
    {
        private readonly ILogger _logger = NullLogger.Instance;
        private readonly List<string> _registeredHostIds = new();

        /// <summary>
        /// Creates a HostOnline with a session and registers it in HostContainer.HostsOnline.
        /// Uses a unique host ID to avoid conflicts with parallel test classes.
        /// </summary>
        private (HostOnline host, string hostId) CreateAndRegisterHost(string sessionId)
        {
            string hostId = $"prc-{Guid.NewGuid():N}";
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = hostId, Name = "TestHost" }
            };
            host.AddSession(new DaisiSession { Id = sessionId });
            HostContainer.HostsOnline.TryAdd(hostId, host);
            _registeredHostIds.Add(hostId);
            return (host, hostId);
        }

        private HostOnline CreateHostLocal(string hostId, string sessionId)
        {
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = hostId, Name = "TestHost" }
            };
            host.AddSession(new DaisiSession { Id = sessionId });
            return host;
        }

        public void Dispose()
        {
            foreach (var id in _registeredHostIds)
                HostContainer.HostsOnline.TryRemove(id, out _);
        }

        #region SessionIncomingQueueHandler: Per-Request Channel Routing

        [Fact]
        public async Task SessionIncomingQueueHandler_RoutesToPerRequestChannel_WhenRequestIdMatches()
        {
            var (host, hostId) = CreateAndRegisterHost("sess-1");

            // Register a per-request channel
            var requestChannel = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd("req-42", requestChannel);

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            var command = new Command
            {
                Name = nameof(ConnectResponse),
                SessionId = "sess-1",
                RequestId = "req-42",
                Payload = Any.Pack(new ConnectResponse { Id = "sess-1", HasCapacity = true })
            };

            await handler.HandleAsync(hostId, command, responseChannel.Writer);

            // Should arrive in the per-request channel
            Assert.True(requestChannel.Reader.TryRead(out var routed));
            Assert.Equal(nameof(ConnectResponse), routed!.Name);
            Assert.Equal("req-42", routed.RequestId);

            // Should NOT be in the session channel
            Assert.False(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out _));
        }

        [Fact]
        public async Task SessionIncomingQueueHandler_FallsBackToSessionChannel_WhenNoPerRequestChannel()
        {
            var (host, hostId) = CreateAndRegisterHost("sess-1");

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            var command = new Command
            {
                Name = nameof(SendInferenceResponse),
                SessionId = "sess-1",
                RequestId = "req-unknown",
                Payload = Any.Pack(new SendInferenceResponse { Content = "hello" })
            };

            await handler.HandleAsync(hostId, command, responseChannel.Writer);

            // No per-request channel for "req-unknown", so falls back to session channel
            Assert.True(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out var routed));
            Assert.Equal(nameof(SendInferenceResponse), routed!.Name);
        }

        [Fact]
        public async Task SessionIncomingQueueHandler_FallsBackToSessionChannel_WhenRequestIdEmpty()
        {
            var (host, hostId) = CreateAndRegisterHost("sess-1");

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            var command = new Command
            {
                Name = nameof(SendInferenceResponse),
                SessionId = "sess-1",
                RequestId = "", // Empty request ID
                Payload = Any.Pack(new SendInferenceResponse { Content = "test" })
            };

            await handler.HandleAsync(hostId, command, responseChannel.Writer);

            // Empty RequestId should fall back to session channel
            Assert.True(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out var routed));
            Assert.Equal(nameof(SendInferenceResponse), routed!.Name);
        }

        [Fact]
        public async Task SessionIncomingQueueHandler_ReturnsGracefully_WhenHostNotFound()
        {
            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            var command = new Command
            {
                Name = nameof(SendInferenceResponse),
                SessionId = "sess-1",
                RequestId = "req-1"
            };

            // Should not throw — host "h-missing-xxx" is not in HostsOnline
            await handler.HandleAsync($"h-missing-{Guid.NewGuid():N}", command, responseChannel.Writer);
        }

        [Fact]
        public async Task SessionIncomingQueueHandler_ReturnsGracefully_WhenSessionChannelNotFound()
        {
            string hostId = $"prc-{Guid.NewGuid():N}";
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = hostId, Name = "TestHost" }
            };
            // No sessions added — SessionIncomingQueues is empty
            HostContainer.HostsOnline.TryAdd(hostId, host);
            _registeredHostIds.Add(hostId);

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            var command = new Command
            {
                Name = nameof(SendInferenceResponse),
                SessionId = "sess-nonexistent",
                RequestId = "req-no-match"
            };

            // Should not throw — logs warning and returns
            await handler.HandleAsync(hostId, command, responseChannel.Writer);
        }

        #endregion

        #region HostOnline.RequestChannels Lifecycle

        [Fact]
        public void RequestChannels_AddAndRetrieve()
        {
            var host = CreateHostLocal("h-local", "sess-1");

            var channel = Channel.CreateUnbounded<Command>();
            Assert.True(host.RequestChannels.TryAdd("req-1", channel));
            Assert.True(host.RequestChannels.TryGetValue("req-1", out var retrieved));
            Assert.Same(channel, retrieved);
        }

        [Fact]
        public void RequestChannels_RemoveCleanup()
        {
            var host = CreateHostLocal("h-local", "sess-1");

            var channel = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd("req-1", channel);

            Assert.True(host.RequestChannels.TryRemove("req-1", out _));
            Assert.False(host.RequestChannels.ContainsKey("req-1"));
        }

        [Fact]
        public void RequestChannels_MultipleCoexist()
        {
            var host = CreateHostLocal("h-local", "sess-1");

            var ch1 = Channel.CreateUnbounded<Command>();
            var ch2 = Channel.CreateUnbounded<Command>();
            var ch3 = Channel.CreateUnbounded<Command>();

            host.RequestChannels.TryAdd("req-A", ch1);
            host.RequestChannels.TryAdd("req-B", ch2);
            host.RequestChannels.TryAdd("req-C", ch3);

            Assert.Equal(3, host.RequestChannels.Count);

            // Remove one, others remain
            host.RequestChannels.TryRemove("req-B", out _);
            Assert.Equal(2, host.RequestChannels.Count);
            Assert.True(host.RequestChannels.ContainsKey("req-A"));
            Assert.False(host.RequestChannels.ContainsKey("req-B"));
            Assert.True(host.RequestChannels.ContainsKey("req-C"));
        }

        #endregion

        #region Per-Request Channel Isolation (Core Bug Fix)

        [Fact]
        public async Task PerRequestChannels_IsolatesConcurrentRequests()
        {
            // This is the core bug fix test: two concurrent requests on the same
            // session should each receive only their own responses.

            var (host, hostId) = CreateAndRegisterHost("sess-1");

            var channelA = Channel.CreateUnbounded<Command>();
            var channelB = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd("req-A", channelA);
            host.RequestChannels.TryAdd("req-B", channelB);

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            // Interleave responses for req-A and req-B
            var commands = new[]
            {
                new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-A", Payload = Any.Pack(new SendInferenceResponse { Content = "A-1" }) },
                new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-B", Payload = Any.Pack(new SendInferenceResponse { Content = "B-1" }) },
                new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-A", Payload = Any.Pack(new SendInferenceResponse { Content = "A-2" }) },
                new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-B", Payload = Any.Pack(new SendInferenceResponse { Content = "B-2" }) },
                new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-A", Payload = Any.Pack(new SendInferenceResponse { Content = "A-3" }) },
            };

            foreach (var cmd in commands)
                await handler.HandleAsync(hostId, cmd, responseChannel.Writer);

            // req-A channel should have exactly 3 messages
            var aMessages = new List<Command>();
            while (channelA.Reader.TryRead(out var msg)) aMessages.Add(msg);
            Assert.Equal(3, aMessages.Count);
            Assert.All(aMessages, m => Assert.Equal("req-A", m.RequestId));

            // req-B channel should have exactly 2 messages
            var bMessages = new List<Command>();
            while (channelB.Reader.TryRead(out var msg)) bMessages.Add(msg);
            Assert.Equal(2, bMessages.Count);
            Assert.All(bMessages, m => Assert.Equal("req-B", m.RequestId));

            // Session channel should have 0 messages (all routed to per-request channels)
            Assert.False(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out _));
        }

        [Fact]
        public async Task PerRequestChannels_CleanedUpRequestFallsToSessionChannel()
        {
            // After a per-request channel is removed (cleanup in finally block),
            // any late-arriving messages should fall back to the session channel.

            var (host, hostId) = CreateAndRegisterHost("sess-1");

            var requestChannel = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd("req-1", requestChannel);

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            // First message goes to per-request channel
            var cmd1 = new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-1" };
            await handler.HandleAsync(hostId, cmd1, responseChannel.Writer);
            Assert.True(requestChannel.Reader.TryRead(out _));

            // Simulate cleanup (as done in SendToHostAndWaitAsync finally block)
            host.RequestChannels.TryRemove("req-1", out _);

            // Late-arriving message with same RequestId should fall back to session channel
            var cmd2 = new Command { Name = "resp", SessionId = "sess-1", RequestId = "req-1" };
            await handler.HandleAsync(hostId, cmd2, responseChannel.Writer);
            Assert.True(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out var fallback));
            Assert.Equal("req-1", fallback!.RequestId);
        }

        #endregion

        #region End-to-End Per-Request Channel Simulation

        [Fact]
        public async Task EndToEnd_PerRequestChannel_RequestResponseFlow()
        {
            // Simulates the full flow:
            // 1. ORC creates per-request channel and sends command to host
            // 2. Host processes and sends response back
            // 3. SessionIncomingQueueHandler routes response to per-request channel
            // 4. ORC reads response from per-request channel
            // 5. ORC cleans up per-request channel

            var (host, hostId) = CreateAndRegisterHost("sess-1");

            // Step 1: ORC creates per-request channel
            string requestId = "req-e2e-test";
            var requestChannel = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd(requestId, requestChannel);

            // Step 2: ORC enqueues command to host (simulate)
            var outgoingCmd = new Command
            {
                Name = nameof(ConnectRequest),
                SessionId = "sess-1",
                RequestId = requestId,
                Payload = Any.Pack(new ConnectRequest { SessionId = "sess-1" })
            };
            host.SessionOutgoingQueues["sess-1"].Writer.TryWrite(outgoingCmd);

            // Verify command is queued for host
            Assert.True(host.SessionOutgoingQueues["sess-1"].Reader.TryRead(out var sentCmd));
            Assert.Equal(requestId, sentCmd!.RequestId);

            // Step 3: Host responds (simulate host sending ConnectResponse back)
            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            var responseCmd = new Command
            {
                Name = nameof(ConnectResponse),
                SessionId = "sess-1",
                RequestId = requestId,
                Payload = Any.Pack(new ConnectResponse { Id = "sess-1", HasCapacity = true })
            };
            await handler.HandleAsync(hostId, responseCmd, responseChannel.Writer);

            // Step 4: ORC reads response from per-request channel
            Assert.True(requestChannel.Reader.TryRead(out var received));
            Assert.Equal(nameof(ConnectResponse), received!.Name);
            var connectResponse = received.Payload.Unpack<ConnectResponse>();
            Assert.Equal("sess-1", connectResponse.Id);
            Assert.True(connectResponse.HasCapacity);

            // Step 5: ORC cleans up
            host.RequestChannels.TryRemove(requestId, out _);
            Assert.False(host.RequestChannels.ContainsKey(requestId));
        }

        [Fact]
        public async Task EndToEnd_PerRequestChannel_StreamingResponseFlow()
        {
            // Simulates streaming inference: multiple responses + ENDSTREAM
            // all routed to the same per-request channel

            var (host, hostId) = CreateAndRegisterHost("sess-1");

            string requestId = "req-stream-e2e";
            var requestChannel = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd(requestId, requestChannel);

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            // Simulate host streaming back 5 tokens + ENDSTREAM
            for (int i = 0; i < 5; i++)
            {
                await handler.HandleAsync(hostId, new Command
                {
                    Name = nameof(SendInferenceResponse),
                    SessionId = "sess-1",
                    RequestId = requestId,
                    Payload = Any.Pack(new SendInferenceResponse { Content = $"token-{i}" })
                }, responseChannel.Writer);
            }

            await handler.HandleAsync(hostId, new Command
            {
                Name = "ENDSTREAM",
                SessionId = "sess-1",
                RequestId = requestId,
                Payload = Any.Pack(new Empty())
            }, responseChannel.Writer);

            // All 6 messages should be in the per-request channel
            var received = new List<Command>();
            while (requestChannel.Reader.TryRead(out var msg)) received.Add(msg);

            Assert.Equal(6, received.Count);
            Assert.Equal(5, received.Count(c => c.Name == nameof(SendInferenceResponse)));
            Assert.Single(received, c => c.Name == "ENDSTREAM");

            // Verify token content
            var firstToken = received[0].Payload.Unpack<SendInferenceResponse>();
            Assert.Equal("token-0", firstToken.Content);

            // Session channel should be empty
            Assert.False(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out _));

            // Cleanup
            host.RequestChannels.TryRemove(requestId, out _);
        }

        [Fact]
        public async Task EndToEnd_ConcurrentStreaming_NoMessageLoss()
        {
            // Two concurrent streaming requests on the same session.
            // This is the exact scenario that caused the original bug.

            var (host, hostId) = CreateAndRegisterHost("sess-1");

            var channelA = Channel.CreateUnbounded<Command>();
            var channelB = Channel.CreateUnbounded<Command>();
            host.RequestChannels.TryAdd("req-A", channelA);
            host.RequestChannels.TryAdd("req-B", channelB);

            var handler = new SessionIncomingQueueHandler(NullLogger<SessionIncomingQueueHandler>.Instance);
            var responseChannel = Channel.CreateUnbounded<Command>();

            // Simulate interleaved streaming from host for both requests
            var tasks = new List<Task>();
            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await handler.HandleAsync(hostId, new Command
                    {
                        Name = nameof(SendInferenceResponse),
                        SessionId = "sess-1",
                        RequestId = "req-A",
                        Payload = Any.Pack(new SendInferenceResponse { Content = $"A-{i}" })
                    }, responseChannel.Writer);
                }
            }));

            tasks.Add(Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    await handler.HandleAsync(hostId, new Command
                    {
                        Name = nameof(SendInferenceResponse),
                        SessionId = "sess-1",
                        RequestId = "req-B",
                        Payload = Any.Pack(new SendInferenceResponse { Content = $"B-{i}" })
                    }, responseChannel.Writer);
                }
            }));

            await Task.WhenAll(tasks);

            // Each channel should have exactly 20 messages — no loss, no cross-contamination
            var aMessages = new List<Command>();
            while (channelA.Reader.TryRead(out var msg)) aMessages.Add(msg);
            Assert.Equal(20, aMessages.Count);
            Assert.All(aMessages, m => Assert.Equal("req-A", m.RequestId));

            var bMessages = new List<Command>();
            while (channelB.Reader.TryRead(out var msg)) bMessages.Add(msg);
            Assert.Equal(20, bMessages.Count);
            Assert.All(bMessages, m => Assert.Equal("req-B", m.RequestId));

            // Session channel should be empty
            Assert.False(host.SessionIncomingQueues["sess-1"].Reader.TryRead(out _));
        }

        #endregion
    }
}
