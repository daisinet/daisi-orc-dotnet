using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Orc.Grpc.CommandServices.Handlers;
using Daisi.Orc.Tests.Fakes;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace Daisi.Orc.Tests.CommandServices
{
    /// <summary>
    /// Tests that ORC-side command handlers correctly write to ChannelWriter<Command>.
    /// </summary>
    public class OrcHandlerChannelTests
    {
        private readonly ILogger _logger = NullLogger.Instance;

        [Fact]
        public async Task SessionIncomingQueueHandler_RoutesToCorrectSessionChannel()
        {
            // Setup HostsOnline with a host and session
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "h1", Name = "TestHost" }
            };
            host.AddSession(new DaisiSession { Id = "sess-1" });
            HostContainer.HostsOnline.TryAdd("h1", host);

            try
            {
                var responseChannel = Channel.CreateUnbounded<Command>();

                // Create handler and simulate incoming command
                var handler = new SessionIncomingQueueHandler(
                    NullLogger<SessionIncomingQueueHandler>.Instance);

                var command = new Command
                {
                    Name = nameof(SendInferenceResponse),
                    SessionId = "sess-1",
                    RequestId = "req-1",
                    Payload = Any.Pack(new SendInferenceResponse { Content = "test" })
                };

                await handler.HandleAsync("h1", command, responseChannel.Writer);

                // Verify the command was routed to the session's incoming queue
                var routed = await host.SessionIncomingQueues["sess-1"].Reader.ReadAsync();
                Assert.Equal(nameof(SendInferenceResponse), routed.Name);
                Assert.Equal("req-1", routed.RequestId);
            }
            finally
            {
                HostContainer.HostsOnline.TryRemove("h1", out _);
            }
        }

        [Fact]
        public async Task ChannelWriter_TryWrite_ReturnsTrueForUnbounded()
        {
            var channel = Channel.CreateUnbounded<Command>();

            bool result = channel.Writer.TryWrite(new Command { Name = "Test" });

            Assert.True(result);
        }

        [Fact]
        public async Task ChannelWriter_TryWrite_ReturnsFalseAfterComplete()
        {
            var channel = Channel.CreateUnbounded<Command>();
            channel.Writer.Complete();

            bool result = channel.Writer.TryWrite(new Command { Name = "Test" });

            Assert.False(result);
        }

        [Fact]
        public async Task EnvironmentHandler_HandleHostUpdaterCheck_WritesToChannelWriter()
        {
            // Test the static method that was updated from ConcurrentQueue to ChannelWriter
            var channel = Channel.CreateUnbounded<Command>();
            var fakeCosmo = new FakeCosmo();

            var host = new Daisi.Orc.Core.Data.Models.Host
            {
                OperatingSystem = "Windows",
                AppVersion = "0.0.1",
                ReleaseGroup = null
            };

            // Build a minimal configuration with a minimum version
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Daisi:MinimumHostVersion", "99.0.0" }
            });
            var config = configBuilder.Build();

            await EnvironmentRequestCommandHandler.HandleHostUpdaterCheckAsync(channel.Writer, host, fakeCosmo, config, NullLogger.Instance);

            // Since version 0.0.1 < 99.0.0, an update command should have been written
            Assert.True(channel.Reader.TryRead(out var cmd));
            Assert.Equal(nameof(UpdateRequiredRequest), cmd!.Name);
        }

        [Fact]
        public async Task EnvironmentHandler_HandleHostUpdaterCheck_NoUpdateWhenCurrent()
        {
            var channel = Channel.CreateUnbounded<Command>();
            var fakeCosmo = new FakeCosmo();

            var host = new Daisi.Orc.Core.Data.Models.Host
            {
                OperatingSystem = "Windows",
                AppVersion = "99.0.0",
                ReleaseGroup = null
            };

            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Daisi:MinimumHostVersion", "1.0.0" }
            });
            var config = configBuilder.Build();

            await EnvironmentRequestCommandHandler.HandleHostUpdaterCheckAsync(channel.Writer, host, fakeCosmo, config, NullLogger.Instance);

            // Version 99.0.0 >= 1.0.0, no update needed
            Assert.False(channel.Reader.TryRead(out _));
        }
    }
}
