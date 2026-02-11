using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Daisi.Orc.Core.Services
{
    public class GitHubReleaseService
    {
        private readonly HttpClient _httpClient;
        private readonly string _orgName;
        private readonly string _repoName;
        private readonly string _workflowFileName;
        private readonly ILogger<GitHubReleaseService> _logger;

        public GitHubReleaseService(IConfiguration configuration, ILogger<GitHubReleaseService> logger)
        {
            _logger = logger;
            _orgName = configuration["GitHub:OrgName"] ?? "daisinet";
            _repoName = "daisi-orc-dotnet";
            _workflowFileName = "orchestrate-release.yml";

            var pat = configuration["GitHub:ReleasePAT"]
                ?? throw new InvalidOperationException("GitHub:ReleasePAT configuration is required");

            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("https://api.github.com/");
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DaisiOrc", "1.0"));
            _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<bool> TriggerOrchestrateReleaseAsync(
            string version,
            string releaseGroup,
            string? releaseNotes,
            bool activate)
        {
            var payload = new
            {
                @ref = "main",
                inputs = new Dictionary<string, string>
                {
                    ["version"] = version,
                    ["release_group"] = releaseGroup,
                    ["release_notes"] = releaseNotes ?? "",
                    ["activate"] = activate.ToString().ToLowerInvariant()
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"repos/{_orgName}/{_repoName}/actions/workflows/{_workflowFileName}/dispatches";

            _logger.LogInformation(
                "Triggering orchestrate-release workflow: version={Version}, group={Group}, activate={Activate}",
                version, releaseGroup, activate);

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully dispatched orchestrate-release workflow for version {Version}", version);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Failed to dispatch orchestrate-release workflow. Status: {StatusCode}, Body: {Body}",
                response.StatusCode, responseBody);

            return false;
        }
    }
}
