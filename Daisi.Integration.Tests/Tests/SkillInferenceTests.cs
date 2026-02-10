using Daisi.Integration.Tests.Infrastructure;
using Daisi.Protos.V1;

namespace Daisi.Integration.Tests.Tests;

/// <summary>
/// Tests skill-like inference scenarios that exercise tool groups end-to-end.
/// With the simulated host, these validate the gRPC pipeline; with a real model,
/// they test actual skill execution.
/// </summary>
[Trait("Category", "RequiresModel")]
[Collection("Integration")]
public class SkillInferenceTests
{
    private readonly IntegrationTestFixture _fixture;

    public SkillInferenceTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Summarize_Prompt_Returns_Response()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        var longText = string.Join(" ", Enumerable.Repeat(
            "The quick brown fox jumps over the lazy dog.", 20));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, $"Summarize this: {longText}", cts.Token);

        Assert.NotEmpty(responses);
        var fullText = string.Join("", responses.Select(r => r.Content));
        Assert.False(string.IsNullOrEmpty(fullText));
    }

    [Fact]
    public async Task Code_Review_Prompt_Returns_Response()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        var code = @"
public void ProcessItems(List<string> items)
{
    for (int i = 0; i <= items.Count; i++)
    {
        Console.WriteLine(items[i]);
    }
}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId,
            $"Review this code for bugs: {code}", cts.Token);

        Assert.NotEmpty(responses);
    }

    [Fact]
    public async Task Basic_Inference_Returns_Text()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.Basic);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Write a haiku about programming", cts.Token);

        Assert.NotEmpty(responses);
        var fullText = string.Join("", responses.Select(r => r.Content));
        Assert.False(string.IsNullOrEmpty(fullText));
    }

    [Fact]
    public async Task Skill_Inference_With_Stats()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "What is 5 + 3?", cts.Token);

        var stats = await _fixture.AppClient.GetInferenceStatsAsync(
            sessionId, inferenceResp.InferenceId);

        Assert.NotNull(stats);
        Assert.True(stats.LastMessageTokenCount > 0);
    }
}
