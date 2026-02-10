using Daisi.Integration.Tests.Infrastructure;
using Daisi.Orc.Grpc.CommandServices.Containers;

namespace Daisi.Integration.Tests.Tests;

[Collection("Integration")]
public class ConcurrencyTests
{
    private readonly IntegrationTestFixture _fixture;

    public ConcurrencyTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Two_Sessions_On_Same_Host_Run_Concurrently()
    {
        // Use two different user keys so the ORC creates two distinct sessions
        // (the ORC reuses sessions for the same client key + host pair)
        var userKey1 = $"client-conc-user1-{Guid.NewGuid():N}";
        var userKey2 = $"client-conc-user2-{Guid.NewGuid():N}";
        _fixture.Server.TestCosmo.SeedUserKey(userKey1, "conc-user-1", OrcTestServer.TestAccountId);
        _fixture.Server.TestCosmo.SeedUserKey(userKey2, "conc-user-2", OrcTestServer.TestAccountId);

        var app1 = new TestAppClient(_fixture.Channel, userKey1);
        var app2 = new TestAppClient(_fixture.Channel, userKey2);

        var (session1, _) = await app1.CreateAndClaimSessionAsync();
        var (session2, _) = await app2.CreateAndClaimSessionAsync();

        Assert.NotEqual(session1, session2);

        // Create inference on both sessions
        var inf1 = await app1.CreateInferenceAsync(session1);
        var inf2 = await app2.CreateInferenceAsync(session2);

        Assert.NotNull(inf1);
        Assert.NotNull(inf2);
        Assert.NotEqual(inf1.InferenceId, inf2.InferenceId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Run both inferences concurrently
        var task1 = app1.SendInferenceAndCollectResponseAsync(
            session1, inf1.InferenceId, "Hello from session 1", cts.Token);
        var task2 = app2.SendInferenceAndCollectResponseAsync(
            session2, inf2.InferenceId, "Hello from session 2", cts.Token);

        var results = await Task.WhenAll(task1, task2);

        Assert.NotEmpty(results[0]);
        Assert.NotEmpty(results[1]);
    }

    [Fact]
    public async Task Multiple_Hosts_Connect_Simultaneously()
    {
        var hostIds = new[] { "host-conc-1", "host-conc-2", "host-conc-3" };
        var hosts = new List<TestHostClient>();
        var channels = new List<Grpc.Net.Client.GrpcChannel>();

        try
        {
            foreach (var hostId in hostIds)
            {
                var clientKey = $"client-{hostId}";
                _fixture.Server.TestCosmo.SeedHost(hostId, OrcTestServer.TestAccountId);
                _fixture.Server.TestCosmo.SeedHostKey(clientKey, hostId, OrcTestServer.TestAccountId);

                var channel = _fixture.Server.CreateGrpcChannel();
                channels.Add(channel);

                var host = new TestHostClient(channel, clientKey);
                await host.ConnectAsync();
                hosts.Add(host);
            }

            // Wait for all hosts to register
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < timeout)
            {
                if (hostIds.All(id => HostContainer.HostsOnline.ContainsKey(id)))
                    break;
                await Task.Delay(100);
            }

            foreach (var hostId in hostIds)
            {
                Assert.True(HostContainer.HostsOnline.ContainsKey(hostId),
                    $"Host {hostId} should be registered");
            }
        }
        finally
        {
            foreach (var host in hosts)
                await host.DisposeAsync();
            foreach (var channel in channels)
                channel.Dispose();
        }
    }

    [Fact]
    public async Task Session_Cleanup_Does_Not_Interfere_With_Active_Sessions()
    {
        // Use a fresh user key to get a distinct session
        var userKey = $"client-cleanup-user-{Guid.NewGuid():N}";
        _fixture.Server.TestCosmo.SeedUserKey(userKey, "cleanup-user", OrcTestServer.TestAccountId);
        var appClient = new TestAppClient(_fixture.Channel, userKey);

        // Create and use a session
        var (activeSession, _) = await appClient.CreateAndClaimSessionAsync();
        var inf = await appClient.CreateInferenceAsync(activeSession);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await appClient.SendInferenceAndCollectResponseAsync(
            activeSession, inf.InferenceId, "Test prompt", cts.Token);

        Assert.NotEmpty(responses);

        // The session should still be accessible
        Assert.True(SessionContainer.TryGet(activeSession, out var session));
        Assert.NotNull(session);
        Assert.False(session.IsExpired);
    }
}
