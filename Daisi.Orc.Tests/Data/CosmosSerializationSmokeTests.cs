using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Data.Models.Skills;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace Daisi.Orc.Tests.Data;

/// <summary>
/// Integration smoke tests that read from the real Cosmos DB to verify
/// System.Text.Json deserialization works after removing Newtonsoft.Json.
///
/// These tests require a valid connection string in user secrets:
///   dotnet user-secrets set "Cosmo:ConnectionString" "..." --project Daisi.Orc.Tests
///
/// Skip trait: set SKIP_COSMOS_INTEGRATION=true to skip in CI.
/// </summary>
[Trait("Category", "Integration")]
public class CosmosSerializationSmokeTests : IDisposable
{
    private readonly Cosmo? _cosmo;
    private readonly bool _skip;

    public CosmosSerializationSmokeTests()
    {
        if (Environment.GetEnvironmentVariable("SKIP_COSMOS_INTEGRATION") == "true")
        {
            _skip = true;
            return;
        }

        var config = new ConfigurationBuilder()
            .AddUserSecrets<CosmosSerializationSmokeTests>()
            .Build();

        var connectionString = config["Cosmo:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _skip = true;
            return;
        }

        _cosmo = new Cosmo(config);
    }

    public void Dispose() { }

    [Fact]
    public async Task ReadModels_DeserializesCorrectly()
    {
        if (_skip) return;

        var models = await _cosmo!.GetAllModelsAsync();

        Assert.NotNull(models);
        Assert.NotEmpty(models);

        var first = models[0];
        Assert.False(string.IsNullOrEmpty(first.Id), "Model.Id should not be empty");
        Assert.False(string.IsNullOrEmpty(first.Name), "Model.Name should not be empty");
        Assert.False(string.IsNullOrEmpty(first.FileName), "Model.FileName should not be empty");
    }

    [Fact]
    public async Task ReadOrcs_DeserializesCorrectly()
    {
        if (_skip) return;

        var container = await _cosmo!.GetContainerAsync(Cosmo.OrcsContainerName);
        var query = new QueryDefinition("SELECT TOP 1 * FROM c");

        using var iterator = container.GetItemQueryIterator<Orchestrator>(query);
        var results = new List<Orchestrator>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        Assert.NotNull(results);
        Assert.NotEmpty(results);

        var first = results[0];
        Assert.False(string.IsNullOrEmpty(first.Id), "Orchestrator.Id should not be empty");
        Assert.False(string.IsNullOrEmpty(first.Name), "Orchestrator.Name should not be empty");
    }

    [Fact]
    public async Task ReadSkills_DeserializesCorrectly()
    {
        if (_skip) return;

        var skills = await _cosmo!.GetPublicApprovedSkillsAsync();

        Assert.NotNull(skills);
        // Skills container may be empty — just verify the query works without errors
        if (skills.Count > 0)
        {
            var first = skills[0];
            Assert.False(string.IsNullOrEmpty(first.Id), "Skill.Id should not be empty");
            Assert.False(string.IsNullOrEmpty(first.Name), "Skill.Name should not be empty");
        }
    }

    [Fact]
    public async Task ReadHosts_DeserializesCorrectly()
    {
        if (_skip) return;

        var container = await _cosmo!.GetContainerAsync(Cosmo.HostsContainerName);
        var query = new QueryDefinition("SELECT TOP 1 * FROM c");

        using var iterator = container.GetItemQueryIterator<Host>(query);
        var results = new List<Host>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        Assert.NotNull(results);
        Assert.NotEmpty(results);

        var first = results[0];
        Assert.False(string.IsNullOrEmpty(first.Id), "Host.Id should not be empty");
        Assert.False(string.IsNullOrEmpty(first.AccountId), "Host.AccountId should not be empty");
    }

    [Fact]
    public async Task ReadAccounts_DeserializesCorrectly()
    {
        if (_skip) return;

        var container = await _cosmo!.GetContainerAsync(Cosmo.AccountsContainerName);
        var query = new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.type = 'Account'");

        using var iterator = container.GetItemQueryIterator<Account>(query);
        var results = new List<Account>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        Assert.NotNull(results);
        Assert.NotEmpty(results);

        var first = results[0];
        Assert.False(string.IsNullOrEmpty(first.Id), "Account.Id should not be empty");
        Assert.Equal("Account", first.type);
    }

    [Fact]
    public async Task ReadCreditAccounts_DeserializesCorrectly()
    {
        if (_skip) return;

        var container = await _cosmo!.GetContainerAsync(Cosmo.CreditsContainerName);
        var query = new QueryDefinition("SELECT TOP 1 * FROM c WHERE IS_DEFINED(c.Balance)");

        using var iterator = container.GetItemQueryIterator<CreditAccount>(query);
        var results = new List<CreditAccount>();
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            results.AddRange(response);
        }

        Assert.NotNull(results);
        if (results.Count > 0)
        {
            var first = results[0];
            Assert.False(string.IsNullOrEmpty(first.Id), "CreditAccount.Id should not be empty");
            Assert.False(string.IsNullOrEmpty(first.AccountId), "CreditAccount.AccountId should not be empty");
        }
    }
}
