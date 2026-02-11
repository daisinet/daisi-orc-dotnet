using Daisi.Orc.Core.Data.Models.Skills;
using Daisi.SDK.Extensions;
using Microsoft.Azure.Cosmos;

namespace Daisi.Orc.Core.Data.Db;

public partial class Cosmo
{
    public const string SkillsIdPrefix = "skill";
    public const string SkillsContainerName = "Skills";
    public const string SkillsPartitionKeyName = "AccountId";

    public const string SkillReviewsContainerName = "SkillReviews";
    public const string SkillReviewsPartitionKeyName = "SkillId";

    public const string InstalledSkillsContainerName = "InstalledSkills";
    public const string InstalledSkillsPartitionKeyName = "AccountId";

    // --- Skill CRUD ---

    public async Task<Skill> CreateSkillAsync(Skill skill)
    {
        var container = await GetContainerAsync(SkillsContainerName);
        skill.Id = GenerateId(SkillsIdPrefix);
        skill.CreatedAt = DateTime.UtcNow;
        skill.UpdatedAt = DateTime.UtcNow;
        var response = await container.CreateItemAsync(skill, new PartitionKey(skill.AccountId));
        return response.Resource;
    }

    public async Task<Skill?> GetSkillAsync(string id, string accountId)
    {
        try
        {
            var container = await GetContainerAsync(SkillsContainerName);
            var response = await container.ReadItemAsync<Skill>(id, new PartitionKey(accountId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Skill?> GetSkillByIdAsync(string id)
    {
        var container = await GetContainerAsync(SkillsContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id);

        using var resultSet = container.GetItemQueryIterator<Skill>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            if (response.Count > 0)
                return response.First();
        }
        return null;
    }

    public async Task<List<Skill>> GetPublicApprovedSkillsAsync(string? search = null, string? tag = null)
    {
        var container = await GetContainerAsync(SkillsContainerName);
        var queryText = "SELECT * FROM c WHERE c.Visibility = 'Public' AND c.Status = 'Approved'";

        if (!string.IsNullOrWhiteSpace(search))
        {
            queryText += " AND (CONTAINS(LOWER(c.Name), LOWER(@search)) OR CONTAINS(LOWER(c.Description), LOWER(@search)))";
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            queryText += " AND ARRAY_CONTAINS(c.Tags, @tag)";
        }

        queryText += " ORDER BY c.DownloadCount DESC";

        var queryDef = new QueryDefinition(queryText);
        if (!string.IsNullOrWhiteSpace(search))
            queryDef = queryDef.WithParameter("@search", search);
        if (!string.IsNullOrWhiteSpace(tag))
            queryDef = queryDef.WithParameter("@tag", tag);

        var skills = new List<Skill>();
        using var resultSet = container.GetItemQueryIterator<Skill>(queryDef);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            skills.AddRange(response);
        }
        return skills;
    }

    public async Task<List<Skill>> GetSkillsByAccountAsync(string accountId)
    {
        var container = await GetContainerAsync(SkillsContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId ORDER BY c.UpdatedAt DESC")
            .WithParameter("@accountId", accountId);

        var skills = new List<Skill>();
        using var resultSet = container.GetItemQueryIterator<Skill>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            skills.AddRange(response);
        }
        return skills;
    }

    public async Task UpdateSkillAsync(Skill skill)
    {
        var container = await GetContainerAsync(SkillsContainerName);
        skill.UpdatedAt = DateTime.UtcNow;
        await container.UpsertItemAsync(skill, new PartitionKey(skill.AccountId));
    }

    public async Task DeleteSkillAsync(string id, string accountId)
    {
        var container = await GetContainerAsync(SkillsContainerName);
        await container.DeleteItemAsync<Skill>(id, new PartitionKey(accountId));
    }

    public async Task<List<Skill>> GetRequiredSkillsAsync()
    {
        var container = await GetContainerAsync(SkillsContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.IsRequired = true AND c.Status = 'Approved'");

        var skills = new List<Skill>();
        using var resultSet = container.GetItemQueryIterator<Skill>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            skills.AddRange(response);
        }
        return skills;
    }

    // --- Installed Skills ---

    public async Task InstallSkillAsync(string accountId, string skillId)
    {
        var container = await GetContainerAsync(InstalledSkillsContainerName);
        var installed = new InstalledSkill
        {
            Id = $"{accountId}-{skillId}",
            AccountId = accountId,
            SkillId = skillId,
            EnabledAt = DateTime.UtcNow
        };
        await container.UpsertItemAsync(installed, new PartitionKey(accountId));

        // Increment download count
        var skill = await GetSkillByIdAsync(skillId);
        if (skill is not null)
        {
            skill.DownloadCount++;
            await UpdateSkillAsync(skill);
        }
    }

    public async Task UninstallSkillAsync(string accountId, string skillId)
    {
        var container = await GetContainerAsync(InstalledSkillsContainerName);
        try
        {
            await container.DeleteItemAsync<InstalledSkill>($"{accountId}-{skillId}", new PartitionKey(accountId));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already uninstalled
        }
    }

    public async Task<List<InstalledSkill>> GetInstalledSkillsAsync(string accountId)
    {
        var container = await GetContainerAsync(InstalledSkillsContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.AccountId = @accountId")
            .WithParameter("@accountId", accountId);

        var skills = new List<InstalledSkill>();
        using var resultSet = container.GetItemQueryIterator<InstalledSkill>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            skills.AddRange(response);
        }
        return skills;
    }

    // --- Review Workflow ---

    public async Task<List<Skill>> GetPendingReviewSkillsAsync()
    {
        var container = await GetContainerAsync(SkillsContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.Status = 'PendingReview' ORDER BY c.UpdatedAt ASC");

        var skills = new List<Skill>();
        using var resultSet = container.GetItemQueryIterator<Skill>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            skills.AddRange(response);
        }
        return skills;
    }

    public async Task<SkillReview> CreateSkillReviewAsync(SkillReview review)
    {
        var container = await GetContainerAsync(SkillReviewsContainerName);
        review.Id = GenerateId("rev");
        review.Timestamp = DateTime.UtcNow;
        var response = await container.CreateItemAsync(review, new PartitionKey(review.SkillId));
        return response.Resource;
    }

    public async Task<List<SkillReview>> GetSkillReviewsAsync(string skillId)
    {
        var container = await GetContainerAsync(SkillReviewsContainerName);
        var query = new QueryDefinition("SELECT * FROM c WHERE c.SkillId = @skillId ORDER BY c.Timestamp DESC")
            .WithParameter("@skillId", skillId);

        var reviews = new List<SkillReview>();
        using var resultSet = container.GetItemQueryIterator<SkillReview>(query);
        while (resultSet.HasMoreResults)
        {
            var response = await resultSet.ReadNextAsync();
            reviews.AddRange(response);
        }
        return reviews;
    }
}
