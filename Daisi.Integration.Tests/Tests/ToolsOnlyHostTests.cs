using Daisi.Integration.Tests.Infrastructure;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;

namespace Daisi.Integration.Tests.Tests;

/// <summary>
/// Integration tests for tools-only host support: session routing exclusion,
/// tool delegation via ORC, and end-to-end command flow.
/// </summary>
[Collection("Integration")]
public class ToolsOnlyHostTests
{
    private readonly IntegrationTestFixture _fixture;

    public ToolsOnlyHostTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ToolsOnlyHost_Connects_And_Appears_Online()
    {
        var toolsHostId = "host-tools-connect-1";
        var toolsClientKey = "client-tools-connect-key-1";

        var host = _fixture.Server.TestCosmo.SeedHost(toolsHostId, OrcTestServer.TestAccountId);
        host.ToolsOnly = true;
        _fixture.Server.TestCosmo.Hosts[toolsHostId] = host;
        _fixture.Server.TestCosmo.SeedHostKey(toolsClientKey, toolsHostId, OrcTestServer.TestAccountId);

        var channel = _fixture.Server.CreateGrpcChannel();
        await using var toolsHost = new TestToolsOnlyHostClient(channel, toolsClientKey);
        await toolsHost.ConnectAsync();

        // Wait for registration
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!HostContainer.HostsOnline.ContainsKey(toolsHostId) && DateTime.UtcNow < timeout)
            await Task.Delay(100);

        Assert.True(HostContainer.HostsOnline.ContainsKey(toolsHostId));
        Assert.True(toolsHost.IsConnected);

        channel.Dispose();
    }

    [Fact]
    public async Task ToolsOnlyHost_ExcludedFromSessionRouting()
    {
        var toolsHostId = "host-tools-excl-1";
        var toolsClientKey = "client-tools-excl-key-1";

        var host = _fixture.Server.TestCosmo.SeedHost(toolsHostId, OrcTestServer.TestAccountId);
        host.ToolsOnly = true;
        _fixture.Server.TestCosmo.Hosts[toolsHostId] = host;
        _fixture.Server.TestCosmo.SeedHostKey(toolsClientKey, toolsHostId, OrcTestServer.TestAccountId);

        var channel = _fixture.Server.CreateGrpcChannel();
        await using var toolsHost = new TestToolsOnlyHostClient(channel, toolsClientKey);
        await toolsHost.ConnectAsync();

        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!HostContainer.HostsOnline.ContainsKey(toolsHostId) && DateTime.UtcNow < timeout)
            await Task.Delay(100);

        // Create a session — should route to the regular host, not the tools-only host
        var createResp = await _fixture.AppClient.CreateSessionAsync();

        Assert.NotNull(createResp);
        Assert.NotEqual(toolsHostId, createResp.Host.Id);
        Assert.Equal(OrcTestServer.TestHostId, createResp.Host.Id);

        channel.Dispose();
    }

    [Fact]
    public async Task ToolsOnlyHost_AvailableVia_GetNextToolsOnlyHost()
    {
        var toolsHostId = "host-tools-getnext-1";
        var toolsClientKey = "client-tools-getnext-key-1";

        var host = _fixture.Server.TestCosmo.SeedHost(toolsHostId, OrcTestServer.TestAccountId);
        host.ToolsOnly = true;
        _fixture.Server.TestCosmo.Hosts[toolsHostId] = host;
        _fixture.Server.TestCosmo.SeedHostKey(toolsClientKey, toolsHostId, OrcTestServer.TestAccountId);

        var channel = _fixture.Server.CreateGrpcChannel();
        await using var toolsHost = new TestToolsOnlyHostClient(channel, toolsClientKey);
        await toolsHost.ConnectAsync();

        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!HostContainer.HostsOnline.ContainsKey(toolsHostId) && DateTime.UtcNow < timeout)
            await Task.Delay(100);

        // GetNextToolsOnlyHost should find this host
        var result = HostContainer.GetNextToolsOnlyHost(OrcTestServer.TestAccountId);

        Assert.NotNull(result);
        Assert.Equal(toolsHostId, result.Host.Id);
        Assert.True(result.Host.ToolsOnly);

        channel.Dispose();
    }

    [Fact]
    public async Task ToolDelegation_ViaOrc_Succeeds()
    {
        var toolsHostId = "host-tools-deleg-1";
        var toolsClientKey = "client-tools-deleg-key-1";

        var host = _fixture.Server.TestCosmo.SeedHost(toolsHostId, OrcTestServer.TestAccountId);
        host.ToolsOnly = true;
        _fixture.Server.TestCosmo.Hosts[toolsHostId] = host;
        _fixture.Server.TestCosmo.SeedHostKey(toolsClientKey, toolsHostId, OrcTestServer.TestAccountId);

        var channel = _fixture.Server.CreateGrpcChannel();
        await using var toolsHost = new TestToolsOnlyHostClient(channel, toolsClientKey);
        await toolsHost.ConnectAsync();

        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!HostContainer.HostsOnline.ContainsKey(toolsHostId) && DateTime.UtcNow < timeout)
            await Task.Delay(100);

        Assert.True(HostContainer.HostsOnline.ContainsKey(toolsHostId));

        // Send a tool execution request to the tools-only host via HostContainer
        var request = new ExecuteToolRequest
        {
            ToolId = "test-tool-1",
            RequestingHostId = OrcTestServer.TestHostId,
            SessionId = "test-session",
            RequestId = "test-req-1"
        };
        request.Parameters.Add(new ToolParam { Name = "input", Value = "hello" });

        var response = await HostContainer.SendToolExecutionToHostAsync(toolsHostId, request, millisecondsToWait: 10000);

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Contains("test-tool-1", response.Output);

        // Verify the tools-only host received the request
        Assert.NotEmpty(toolsHost.ReceivedToolRequests);
        Assert.Equal("test-tool-1", toolsHost.ReceivedToolRequests[0].ToolId);

        channel.Dispose();
    }

    [Fact]
    public async Task ToolDelegation_WithParameters_PreservesParams()
    {
        var toolsHostId = "host-tools-params-1";
        var toolsClientKey = "client-tools-params-key-1";

        var host = _fixture.Server.TestCosmo.SeedHost(toolsHostId, OrcTestServer.TestAccountId);
        host.ToolsOnly = true;
        _fixture.Server.TestCosmo.Hosts[toolsHostId] = host;
        _fixture.Server.TestCosmo.SeedHostKey(toolsClientKey, toolsHostId, OrcTestServer.TestAccountId);

        var channel = _fixture.Server.CreateGrpcChannel();
        await using var toolsHost = new TestToolsOnlyHostClient(channel, toolsClientKey);
        await toolsHost.ConnectAsync();

        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (!HostContainer.HostsOnline.ContainsKey(toolsHostId) && DateTime.UtcNow < timeout)
            await Task.Delay(100);

        var request = new ExecuteToolRequest
        {
            ToolId = "multi-param-tool",
            RequestingHostId = OrcTestServer.TestHostId
        };
        request.Parameters.Add(new ToolParam { Name = "param1", Value = "value1" });
        request.Parameters.Add(new ToolParam { Name = "param2", Value = "value2" });

        var response = await HostContainer.SendToolExecutionToHostAsync(toolsHostId, request, millisecondsToWait: 10000);

        Assert.NotNull(response);
        Assert.True(response.Success);

        // Verify all parameters were sent through
        var received = toolsHost.ReceivedToolRequests.Last();
        Assert.Equal(2, received.Parameters.Count);
        Assert.Equal("param1", received.Parameters[0].Name);
        Assert.Equal("value1", received.Parameters[0].Value);
        Assert.Equal("param2", received.Parameters[1].Name);
        Assert.Equal("value2", received.Parameters[1].Value);

        channel.Dispose();
    }
}
