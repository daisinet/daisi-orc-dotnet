using Daisi.Integration.Tests.Infrastructure;
using Daisi.Protos.V1;

namespace Daisi.Integration.Tests.Tests;

[Collection("Integration")]
public class InferenceRoundTripTests
{
    private readonly IntegrationTestFixture _fixture;

    public InferenceRoundTripTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_Inference_Returns_InferenceId()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();

        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        Assert.NotNull(inferenceResp);
        Assert.False(string.IsNullOrEmpty(inferenceResp.InferenceId));
        Assert.StartsWith("inf-", inferenceResp.InferenceId);
    }

    [Fact]
    public async Task Send_Prompt_Receives_Streamed_Tokens()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Hello, how are you?", cts.Token);

        Assert.NotEmpty(responses);
        var fullText = string.Join("", responses.Select(r => r.Content));
        Assert.False(string.IsNullOrEmpty(fullText));
    }

    [Fact]
    public async Task Stats_Returns_Token_Counts()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Hello", cts.Token);

        var stats = await _fixture.AppClient.GetInferenceStatsAsync(
            sessionId, inferenceResp.InferenceId);

        Assert.NotNull(stats);
        Assert.True(stats.LastMessageTokenCount > 0);
        Assert.True(stats.SessionTokenCount > 0);
    }

    [Fact]
    public async Task Close_Inference_Cleans_Up()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        var closeResp = await _fixture.AppClient.CloseInferenceAsync(
            sessionId, inferenceResp.InferenceId);

        Assert.NotNull(closeResp);
        Assert.Equal(inferenceResp.InferenceId, closeResp.InferenceId);
    }

    [Fact]
    public async Task Multiple_Sequential_Prompts_Work()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var responses1 = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "First message", cts.Token);

        var responses2 = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Second message", cts.Token);

        Assert.NotEmpty(responses1);
        Assert.NotEmpty(responses2);
    }
}
