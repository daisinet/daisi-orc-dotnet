using Daisi.Integration.Tests.Infrastructure;
using Daisi.Orc.Grpc.CommandServices.Containers;

namespace Daisi.Integration.Tests.Tests;

[Collection("Integration")]
public class SessionLifecycleTests
{
    private readonly IntegrationTestFixture _fixture;

    public SessionLifecycleTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_Session_Returns_Valid_Id_And_Host()
    {
        var response = await _fixture.AppClient.CreateSessionAsync();

        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.Id));
        Assert.StartsWith("session-", response.Id);
        Assert.NotNull(response.Host);
        Assert.Equal(OrcTestServer.TestHostId, response.Host.Id);
    }

    [Fact]
    public async Task Claim_Session_Succeeds()
    {
        var createResp = await _fixture.AppClient.CreateSessionAsync();
        var claimResp = await _fixture.AppClient.ClaimSessionAsync(createResp.Id);

        Assert.True(claimResp.Success);
    }

    [Fact]
    public async Task Connect_Session_Routes_To_Host()
    {
        var createResp = await _fixture.AppClient.CreateSessionAsync();
        await _fixture.AppClient.ClaimSessionAsync(createResp.Id);
        var connectResp = await _fixture.AppClient.ConnectSessionAsync(createResp.Id);

        Assert.NotNull(connectResp);
        Assert.True(connectResp.HasCapacity);
        Assert.False(connectResp.AlreadyConnected);
    }

    [Fact]
    public async Task Close_Session_Succeeds()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var closeResp = await _fixture.AppClient.CloseSessionAsync(sessionId);

        Assert.True(closeResp.Success);
    }

    [Fact]
    public async Task Create_Session_Fails_For_NonExistent_Host()
    {
        // Requesting a specific host that doesn't exist should fail
        var ex = await Assert.ThrowsAsync<Grpc.Core.RpcException>(async () =>
        {
            await _fixture.AppClient.CreateSessionAsync(hostId: "host-does-not-exist");
        });

        Assert.Contains("No host is online", ex.Status.Detail);
    }
}
