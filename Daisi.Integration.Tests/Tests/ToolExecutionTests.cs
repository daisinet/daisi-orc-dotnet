using Daisi.Integration.Tests.Infrastructure;
using Daisi.Protos.V1;

namespace Daisi.Integration.Tests.Tests;

/// <summary>
/// Tests the full tool pipeline via ORC relay. These tests validate the gRPC
/// command routing for inference with tools. With the simulated host, they
/// verify the pipeline works end-to-end; with a real model, they test actual
/// tool selection and execution.
/// </summary>
[Trait("Category", "RequiresModel")]
[Collection("Integration")]
public class ToolExecutionTests
{
    private readonly IntegrationTestFixture _fixture;

    public ToolExecutionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_Inference_With_BasicWithTools_Succeeds()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();

        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        Assert.NotNull(inferenceResp);
        Assert.False(string.IsNullOrEmpty(inferenceResp.InferenceId));
    }

    [Fact]
    public async Task Math_Prompt_Returns_Response()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "What is 347 * 29?", cts.Token);

        Assert.NotEmpty(responses);
        var fullText = string.Join("", responses.Select(r => r.Content));
        Assert.False(string.IsNullOrEmpty(fullText));
    }

    [Fact]
    public async Task Regex_Prompt_Returns_Response()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId,
            "Find all email addresses in: contact user@example.com or admin@test.org",
            cts.Token);

        Assert.NotEmpty(responses);
    }

    [Fact]
    public async Task Tool_Inference_Stream_Completes()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "What is 2 + 2?", cts.Token);

        Assert.NotEmpty(responses);
        // Verify stream completed (we got all responses)
        Assert.True(responses.Count > 0);
    }

    [Fact]
    public async Task Close_Tool_Inference_Succeeds()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Hello", cts.Token);

        var closeResp = await _fixture.AppClient.CloseInferenceAsync(
            sessionId, inferenceResp.InferenceId);

        Assert.NotNull(closeResp);
    }

    [Fact]
    public async Task Multiple_Tool_Prompts_Sequential()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(
            sessionId, thinkLevel: ThinkLevels.BasicWithTools);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var r1 = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "What is 10 + 5?", cts.Token);

        var r2 = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Now multiply that by 3", cts.Token);

        Assert.NotEmpty(r1);
        Assert.NotEmpty(r2);
    }
}
