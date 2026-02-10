using Daisi.Integration.Tests.Infrastructure;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;

namespace Daisi.Integration.Tests.Tests;

[Collection("Integration")]
public class HostConnectionTests
{
    private readonly IntegrationTestFixture _fixture;

    public HostConnectionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Host_Connects_And_Appears_In_HostsOnline()
    {
        Assert.True(_fixture.HostClient.IsConnected);
        Assert.True(HostContainer.HostsOnline.ContainsKey(OrcTestServer.TestHostId));
    }

    [Fact]
    public void Host_EnvironmentRequest_Is_Recorded()
    {
        Assert.True(HostContainer.HostsOnline.TryGetValue(OrcTestServer.TestHostId, out var hostOnline));
        Assert.NotNull(hostOnline.Host);
        Assert.Equal("Windows", hostOnline.Host.OperatingSystem);
    }

    [Fact]
    public void Host_Status_Is_Online()
    {
        Assert.True(HostContainer.HostsOnline.TryGetValue(OrcTestServer.TestHostId, out var hostOnline));
        Assert.Equal(HostStatus.Online, hostOnline.Host.Status);
    }

    [Fact]
    public async Task Second_Host_Can_Connect_Simultaneously()
    {
        var secondHostId = "host-test-2";
        var secondClientKey = "client-host-test-key-2";

        _fixture.Server.TestCosmo.SeedHost(secondHostId, OrcTestServer.TestAccountId);
        _fixture.Server.TestCosmo.SeedHostKey(secondClientKey, secondHostId, OrcTestServer.TestAccountId);

        var channel = _fixture.Server.CreateGrpcChannel();
        await using var secondHost = new TestHostClient(channel, secondClientKey);
        await secondHost.ConnectAsync();

        // Wait for registration
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!HostContainer.HostsOnline.ContainsKey(secondHostId) && DateTime.UtcNow < timeout)
            await Task.Delay(100);

        Assert.True(HostContainer.HostsOnline.ContainsKey(secondHostId));
        Assert.True(HostContainer.HostsOnline.Count >= 2);

        channel.Dispose();
    }
}
