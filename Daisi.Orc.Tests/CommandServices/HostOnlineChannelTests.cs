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
    /// Tests the Channel<T>-based HostOnline class that replaced ConcurrentQueue polling.
    /// </summary>
    public class HostOnlineChannelTests
    {
        private readonly ILogger _logger = NullLogger.Instance;

        private HostOnline CreateHostOnline()
        {
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "host-1", Name = "TestHost" }
            };
            return host;
        }

        [Fact]
        public void AddSession_CreatesChannelQueues()
        {
            var host = CreateHostOnline();
            var session = new DaisiSession { Id = "sess-1" };

            host.AddSession(session);

            Assert.True(host.SessionOutgoingQueues.ContainsKey("sess-1"));
            Assert.True(host.SessionIncomingQueues.ContainsKey("sess-1"));
        }

        [Fact]
        public void CloseSession_RemovesQueuesAndCompletes()
        {
            var host = CreateHostOnline();
            var session = new DaisiSession { Id = "sess-1" };
            host.AddSession(session);

            host.CloseSession("sess-1");

            Assert.False(host.SessionOutgoingQueues.ContainsKey("sess-1"));
            Assert.False(host.SessionIncomingQueues.ContainsKey("sess-1"));
        }

        [Fact]
        public async Task OutgoingQueue_WriterTryWrite_ReaderCanRead()
        {
            var host = CreateHostOnline();
            var cmd = new Command { Name = "TestCmd", SessionId = "s1" };

            host.OutgoingQueue.Writer.TryWrite(cmd);

            var result = await host.OutgoingQueue.Reader.ReadAsync();
            Assert.Equal("TestCmd", result.Name);
        }

        [Fact]
        public async Task SessionOutgoingQueue_WriterTryWrite_ReaderCanRead()
        {
            var host = CreateHostOnline();
            var session = new DaisiSession { Id = "sess-1" };
            host.AddSession(session);

            var cmd = new Command { Name = "SessionCmd", SessionId = "sess-1" };
            host.SessionOutgoingQueues["sess-1"].Writer.TryWrite(cmd);

            var result = await host.SessionOutgoingQueues["sess-1"].Reader.ReadAsync();
            Assert.Equal("SessionCmd", result.Name);
        }

        [Fact]
        public async Task SessionIncomingQueue_WriterTryWrite_ReaderCanRead()
        {
            var host = CreateHostOnline();
            var session = new DaisiSession { Id = "sess-1" };
            host.AddSession(session);

            var cmd = new Command { Name = "IncomingResponse", SessionId = "sess-1", RequestId = "req-1" };
            host.SessionIncomingQueues["sess-1"].Writer.TryWrite(cmd);

            var result = await host.SessionIncomingQueues["sess-1"].Reader.ReadAsync();
            Assert.Equal("IncomingResponse", result.Name);
            Assert.Equal("req-1", result.RequestId);
        }

        [Fact]
        public async Task SendOutgoingCommandsAsync_ForwardsHostCommands()
        {
            var host = CreateHostOnline();
            var fakeStream = new FakeServerStreamWriter<Command>();

            // Write a command to the host's outgoing queue
            host.OutgoingQueue.Writer.TryWrite(new Command { Name = "HostCmd" });
            // Complete the writer to signal no more commands (so SendOutgoing exits)
            host.OutgoingQueue.Writer.Complete();

            using var cts = new CancellationTokenSource(5000);
            await host.SendOutgoingCommandsAsync(fakeStream, cts.Token);

            var written = await fakeStream.WrittenChannel.Reader.ReadAsync();
            Assert.Equal("HostCmd", written.Name);
        }

        [Fact]
        public async Task SendOutgoingCommandsAsync_ForwardsSessionCommands()
        {
            var host = CreateHostOnline();
            var session = new DaisiSession { Id = "sess-1" };
            host.AddSession(session);

            var fakeStream = new FakeServerStreamWriter<Command>();

            // Write a command to the session outgoing queue
            host.SessionOutgoingQueues["sess-1"].Writer.TryWrite(
                new Command { Name = "SessionCmd", SessionId = "sess-1" });

            // Complete both writers so the method exits
            host.SessionOutgoingQueues["sess-1"].Writer.Complete();

            // Give the background forwarder time to start, then complete the host channel
            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                host.OutgoingQueue.Writer.Complete();
            });

            using var cts = new CancellationTokenSource(5000);
            await host.SendOutgoingCommandsAsync(fakeStream, cts.Token);

            var written = await fakeStream.WrittenChannel.Reader.ReadAsync();
            Assert.Equal("SessionCmd", written.Name);
        }

        [Fact]
        public async Task SendOutgoingCommandsAsync_CancelledByCancellationToken()
        {
            var host = CreateHostOnline();
            var fakeStream = new FakeServerStreamWriter<Command>();

            using var cts = new CancellationTokenSource(100);

            // Should exit quickly when cancelled, not hang
            await host.SendOutgoingCommandsAsync(fakeStream, cts.Token);

            // If we get here without timeout, the cancellation worked
            Assert.True(true);
        }

        [Fact]
        public async Task MultipleSessionQueues_AllForwarded()
        {
            var host = CreateHostOnline();
            host.AddSession(new DaisiSession { Id = "s1" });
            host.AddSession(new DaisiSession { Id = "s2" });

            var fakeStream = new FakeServerStreamWriter<Command>();

            host.SessionOutgoingQueues["s1"].Writer.TryWrite(new Command { Name = "Cmd-S1", SessionId = "s1" });
            host.SessionOutgoingQueues["s2"].Writer.TryWrite(new Command { Name = "Cmd-S2", SessionId = "s2" });

            // Complete all writers
            host.SessionOutgoingQueues["s1"].Writer.Complete();
            host.SessionOutgoingQueues["s2"].Writer.Complete();

            _ = Task.Run(async () =>
            {
                await Task.Delay(300);
                host.OutgoingQueue.Writer.Complete();
            });

            using var cts = new CancellationTokenSource(5000);
            await host.SendOutgoingCommandsAsync(fakeStream, cts.Token);

            var received = new List<Command>();
            while (fakeStream.WrittenChannel.Reader.TryRead(out var cmd))
                received.Add(cmd);

            Assert.Equal(2, received.Count);
            Assert.Contains(received, c => c.Name == "Cmd-S1");
            Assert.Contains(received, c => c.Name == "Cmd-S2");
        }
    }
}
