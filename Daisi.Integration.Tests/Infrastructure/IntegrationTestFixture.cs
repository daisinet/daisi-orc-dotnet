using Daisi.Orc.Grpc.CommandServices.Containers;
using Grpc.Net.Client;

namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Shared test fixture that starts the ORC server, connects a test host,
/// and provides a test app client. Shared across tests via IClassFixture.
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    public OrcTestServer Server { get; private set; } = null!;
    public TestHostClient HostClient { get; private set; } = null!;
    public TestAppClient AppClient { get; private set; } = null!;
    public GrpcChannel Channel { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Clear any static state from previous test runs
        HostContainer.HostsOnline.Clear();

        Server = new OrcTestServer();
        Channel = Server.CreateGrpcChannel();

        // Connect the test host
        HostClient = new TestHostClient(Channel, OrcTestServer.TestHostClientKey);
        await HostClient.ConnectAsync();

        // Give the host time to register with the ORC
        await WaitForHostRegistrationAsync();

        // Create app client
        AppClient = new TestAppClient(Channel, OrcTestServer.TestUserClientKey);
    }

    private async Task WaitForHostRegistrationAsync()
    {
        // Wait for the host to appear in HostsOnline
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            if (HostContainer.HostsOnline.ContainsKey(OrcTestServer.TestHostId))
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException("Test host did not register within timeout.");
    }

    public async Task DisposeAsync()
    {
        // Clear static state BEFORE disposing the server to prevent
        // ApplicationStopping handler from crashing when it tries to
        // unregister hosts via the disposed service provider.
        HostContainer.HostsOnline.Clear();

        await HostClient.DisposeAsync();
        Channel.Dispose();
        Server.Dispose();
    }
}
