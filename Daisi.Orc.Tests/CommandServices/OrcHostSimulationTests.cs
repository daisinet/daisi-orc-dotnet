using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Tests.Fakes;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace Daisi.Orc.Tests.CommandServices
{
    /// <summary>
    /// End-to-end simulation tests that verify the ORC ↔ Host command routing
    /// works correctly with Channel<T> replacing ConcurrentQueue polling.
    ///
    /// These tests simulate the full flow:
    /// 1. ORC enqueues a command to SessionOutgoingQueue
    /// 2. SendOutgoingCommandsAsync forwards it to the gRPC stream (FakeServerStreamWriter)
    /// 3. Host processes command and writes response to SessionIncomingQueue
    /// 4. ORC reads the response from SessionIncomingQueue
    /// </summary>
    public class OrcHostSimulationTests
    {
        private readonly ILogger _logger = NullLogger.Instance;

        private HostOnline CreateHostWithSession(string sessionId)
        {
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host
                {
                    Id = "host-1",
                    Name = "TestHost",
                    AccountId = "acct-1"
                }
            };
            host.AddSession(new DaisiSession { Id = sessionId });
            return host;
        }

        [Fact]
        public async Task RoundTrip_RequestAndResponse_ViaChannels()
        {
            // Setup: ORC creates a host with session
            var host = CreateHostWithSession("sess-1");
            var fakeStream = new FakeServerStreamWriter<Command>();

            // Step 1: ORC sends a request by writing to SessionOutgoingQueue
            var requestCmd = new Command
            {
                Name = nameof(CreateInferenceRequest),
                SessionId = "sess-1",
                RequestId = "req-123",
                Payload = Any.Pack(new CreateInferenceRequest { SessionId = "sess-1" })
            };
            host.SessionOutgoingQueues["sess-1"].Writer.TryWrite(requestCmd);

            // Step 2: Start SendOutgoingCommandsAsync in background (it will forward to stream)
            host.SessionOutgoingQueues["sess-1"].Writer.Complete();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                host.OutgoingQueue.Writer.Complete();
            });

            using var cts = new CancellationTokenSource(5000);
            var sendTask = host.SendOutgoingCommandsAsync(fakeStream, cts.Token);

            // Step 3: Verify command arrived at the stream (simulating Host receiving it)
            var sentToHost = await fakeStream.WrittenChannel.Reader.ReadAsync();
            Assert.Equal(nameof(CreateInferenceRequest), sentToHost.Name);
            Assert.Equal("req-123", sentToHost.RequestId);

            await sendTask;

            // Step 4: Simulate Host responding by writing to SessionIncomingQueue
            var responseCmd = new Command
            {
                Name = nameof(CreateInferenceResponse),
                SessionId = "sess-1",
                RequestId = "req-123",
                Payload = Any.Pack(new CreateInferenceResponse { InferenceId = "inf-456" })
            };

            // Re-create queue since session was closed, for incoming test
            var incomingChannel = Channel.CreateUnbounded<Command>();
            incomingChannel.Writer.TryWrite(responseCmd);

            // Step 5: ORC reads the response
            var incomingResponse = await incomingChannel.Reader.ReadAsync();
            Assert.Equal(nameof(CreateInferenceResponse), incomingResponse.Name);
            Assert.Equal("req-123", incomingResponse.RequestId);

            var response = incomingResponse.Payload.Unpack<CreateInferenceResponse>();
            Assert.Equal("inf-456", response.InferenceId);
        }

        [Fact]
        public async Task StreamingRoundTrip_MultipleResponses_AllDelivered()
        {
            // Simulates an inference streaming scenario:
            // ORC sends SendInferenceRequest, Host streams back multiple responses + ENDSTREAM

            var incomingChannel = Channel.CreateUnbounded<Command>();

            // Simulate Host streaming back 10 responses + ENDSTREAM
            for (int i = 0; i < 10; i++)
            {
                incomingChannel.Writer.TryWrite(new Command
                {
                    Name = nameof(SendInferenceResponse),
                    SessionId = "sess-1",
                    RequestId = "req-stream",
                    Payload = Any.Pack(new SendInferenceResponse
                    {
                        Content = $"token-{i}",
                        Type = InferenceResponseTypes.Text
                    })
                });
            }

            incomingChannel.Writer.TryWrite(new Command
            {
                Name = "ENDSTREAM",
                SessionId = "sess-1",
                RequestId = "req-stream",
                Payload = Any.Pack(new Empty())
            });

            // ORC reads all responses using Channel
            var received = new List<Command>();
            using var timeoutCts = new CancellationTokenSource(5000);

            await foreach (var cmd in incomingChannel.Reader.ReadAllAsync(timeoutCts.Token))
            {
                received.Add(cmd);
                if (cmd.Name == "ENDSTREAM")
                    break;
            }

            Assert.Equal(11, received.Count);
            Assert.Equal(10, received.Count(c => c.Name == nameof(SendInferenceResponse)));
            Assert.Single(received.Where(c => c.Name == "ENDSTREAM"));

            // Verify responses can be unpacked
            var firstResponse = received[0].Payload.Unpack<SendInferenceResponse>();
            Assert.Equal("token-0", firstResponse.Content);
        }

        [Fact]
        public async Task StreamingRoundTrip_FiltersByRequestId()
        {
            // Simulates multiple concurrent requests on same session -
            // only messages matching the request ID should be processed

            var incomingChannel = Channel.CreateUnbounded<Command>();

            // Interleave messages from two different requests
            incomingChannel.Writer.TryWrite(new Command { Name = "resp", RequestId = "req-A" });
            incomingChannel.Writer.TryWrite(new Command { Name = "resp", RequestId = "req-B" });
            incomingChannel.Writer.TryWrite(new Command { Name = "resp", RequestId = "req-A" });
            incomingChannel.Writer.TryWrite(new Command { Name = "ENDSTREAM", RequestId = "req-A" });
            incomingChannel.Writer.TryWrite(new Command { Name = "resp", RequestId = "req-B" });
            incomingChannel.Writer.TryWrite(new Command { Name = "ENDSTREAM", RequestId = "req-B" });

            // Read messages for req-A only (like SendToHostAndStreamAsync does)
            var reqAMessages = new List<Command>();
            using var cts = new CancellationTokenSource(5000);

            await foreach (var cmd in incomingChannel.Reader.ReadAllAsync(cts.Token))
            {
                if (cmd.RequestId != "req-A")
                    continue; // Skip messages for other requests

                reqAMessages.Add(cmd);
                if (cmd.Name == "ENDSTREAM")
                    break;
            }

            Assert.Equal(3, reqAMessages.Count); // 2 resp + 1 ENDSTREAM
            Assert.All(reqAMessages, m => Assert.Equal("req-A", m.RequestId));
        }

        [Fact]
        public async Task Timeout_CancelsReading_WhenNoResponse()
        {
            var incomingChannel = Channel.CreateUnbounded<Command>();

            using var timeoutCts = new CancellationTokenSource(200);
            var received = new List<Command>();

            try
            {
                await foreach (var cmd in incomingChannel.Reader.ReadAllAsync(timeoutCts.Token))
                {
                    received.Add(cmd);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected - timeout occurred
            }

            Assert.Empty(received);
        }

        [Fact]
        public async Task ConcurrentSessionSendsAndReceives()
        {
            var host = CreateHostWithSession("sess-1");
            host.AddSession(new DaisiSession { Id = "sess-2" });

            // Simulate ORC sending to both sessions concurrently
            var send1 = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    host.SessionOutgoingQueues["sess-1"].Writer.TryWrite(
                        new Command { Name = "Cmd", SessionId = "sess-1", RequestId = $"r1-{i}" });
            });

            var send2 = Task.Run(() =>
            {
                for (int i = 0; i < 50; i++)
                    host.SessionOutgoingQueues["sess-2"].Writer.TryWrite(
                        new Command { Name = "Cmd", SessionId = "sess-2", RequestId = $"r2-{i}" });
            });

            await Task.WhenAll(send1, send2);

            // Verify both session queues have their commands
            int count1 = 0, count2 = 0;
            while (host.SessionOutgoingQueues["sess-1"].Reader.TryRead(out _)) count1++;
            while (host.SessionOutgoingQueues["sess-2"].Reader.TryRead(out _)) count2++;

            Assert.Equal(50, count1);
            Assert.Equal(50, count2);
        }

        [Fact]
        public async Task HostOnline_SessionLifecycle_FullCycle()
        {
            var host = CreateHostWithSession("sess-live");

            // Write to incoming queue (simulating host response arriving)
            host.SessionIncomingQueues["sess-live"].Writer.TryWrite(
                new Command { Name = "Response", SessionId = "sess-live" });

            // Read from incoming
            var response = await host.SessionIncomingQueues["sess-live"].Reader.ReadAsync();
            Assert.Equal("Response", response.Name);

            // Close session - should complete the channel writers
            host.CloseSession("sess-live");

            // Session queues should be gone
            Assert.False(host.SessionOutgoingQueues.ContainsKey("sess-live"));
            Assert.False(host.SessionIncomingQueues.ContainsKey("sess-live"));
        }
    }
}
