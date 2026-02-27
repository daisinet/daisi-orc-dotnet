using Daisi.Orc.Core.Services;
using Daisi.Orc.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daisi.Orc.Tests.Services;

public class McpServiceTests
{
    private readonly FakeCosmo _cosmo;
    private readonly McpService _service;

    public McpServiceTests()
    {
        _cosmo = new FakeCosmo();
        _service = new McpService(_cosmo, NullLogger<McpService>.Instance);
    }

    [Fact]
    public async Task RegisterServer_CreatesWithCorrectDefaults()
    {
        var server = await _service.RegisterServerAsync(
            "acct-1", "user-1", "John",
            "Test Server", "https://mcp.example.com",
            "NONE", "", 30);

        Assert.StartsWith("mcp-", server.Id);
        Assert.Equal("acct-1", server.AccountId);
        Assert.Equal("Test Server", server.Name);
        Assert.Equal("https://mcp.example.com", server.ServerUrl);
        Assert.Equal("NONE", server.AuthType);
        Assert.Equal(30, server.SyncIntervalMinutes);
        Assert.Equal("PENDING", server.Status);
        Assert.Equal("user-1", server.CreatedByUserId);
        Assert.Equal("John", server.CreatedByUserName);
    }

    [Fact]
    public async Task RegisterServer_DefaultSyncInterval_60Minutes()
    {
        var server = await _service.RegisterServerAsync(
            "acct-1", "user-1", "John",
            "Test", "https://example.com",
            "NONE", "", 0);

        Assert.Equal(60, server.SyncIntervalMinutes);
    }

    [Fact]
    public async Task GetServers_ReturnsOnlyAccountServers()
    {
        await _service.RegisterServerAsync("acct-1", "u1", "U1", "Server A", "https://a.com", "NONE", "", 60);
        await _service.RegisterServerAsync("acct-2", "u2", "U2", "Server B", "https://b.com", "NONE", "", 60);
        await _service.RegisterServerAsync("acct-1", "u1", "U1", "Server C", "https://c.com", "NONE", "", 60);

        var servers = await _service.GetServersAsync("acct-1");

        Assert.Equal(2, servers.Count);
        Assert.All(servers, s => Assert.Equal("acct-1", s.AccountId));
    }

    [Fact]
    public async Task GetServer_ReturnsCorrectServer()
    {
        var created = await _service.RegisterServerAsync(
            "acct-1", "u1", "U1", "Test", "https://test.com", "NONE", "", 60);

        var found = await _service.GetServerAsync(created.Id, "acct-1");

        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);
    }

    [Fact]
    public async Task GetServer_WrongAccount_ReturnsNull()
    {
        var created = await _service.RegisterServerAsync(
            "acct-1", "u1", "U1", "Test", "https://test.com", "NONE", "", 60);

        var found = await _service.GetServerAsync(created.Id, "acct-2");

        Assert.Null(found);
    }

    [Fact]
    public async Task RemoveServer_DeletesFromStore()
    {
        var created = await _service.RegisterServerAsync(
            "acct-1", "u1", "U1", "Test", "https://test.com", "NONE", "", 60);

        await _service.RemoveServerAsync(created.Id, "acct-1");

        var found = await _service.GetServerAsync(created.Id, "acct-1");
        Assert.Null(found);
    }

    [Fact]
    public async Task GetServersDueForSync_PendingWithNoSyncDate_ReturnsDue()
    {
        await _service.RegisterServerAsync(
            "acct-1", "u1", "U1", "Test", "https://test.com", "NONE", "", 60);

        var due = await _service.GetServersDueForSyncAsync();

        Assert.Single(due);
    }

    [Fact]
    public async Task GetServersDueForSync_RecentlySync_NotDue()
    {
        var server = await _service.RegisterServerAsync(
            "acct-1", "u1", "U1", "Test", "https://test.com", "NONE", "", 60);

        // Mark as connected and recently synced
        server.Status = "CONNECTED";
        server.DateLastSync = DateTime.UtcNow;
        await _service.UpdateServerAsync(server);

        var due = await _service.GetServersDueForSyncAsync();

        Assert.Empty(due);
    }

    [Fact]
    public async Task UpdateSyncStatus_UpdatesFields()
    {
        var server = await _service.RegisterServerAsync(
            "acct-1", "u1", "U1", "Test", "https://test.com", "NONE", "", 60);

        await _service.UpdateSyncStatusAsync(server.Id, "acct-1", "CONNECTED",
            lastError: null, resourcesSynced: 5);

        var updated = await _service.GetServerAsync(server.Id, "acct-1");
        Assert.NotNull(updated);
        Assert.Equal("CONNECTED", updated!.Status);
        Assert.Equal(5, updated.ResourcesSynced);
        Assert.NotNull(updated.DateLastSync);
    }
}
