using Daisi.Integration.Tests.Infrastructure;
using Daisi.Protos.V1;

namespace Daisi.Integration.Tests.Tests;

[Collection("Integration")]
public class InferenceStreamingTests
{
    private readonly IntegrationTestFixture _fixture;

    public InferenceStreamingTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Streamed_Response_Contains_Text_Tokens()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Say hello", cts.Token);

        Assert.NotEmpty(responses);
        Assert.All(responses, r =>
        {
            Assert.Equal(InferenceResponseTypes.Text, r.Type);
            Assert.False(string.IsNullOrEmpty(r.Content));
        });
    }

    [Fact]
    public async Task All_Tokens_Have_Valid_InferenceId()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Hello", cts.Token);

        Assert.All(responses, r =>
        {
            Assert.Equal(inferenceResp.InferenceId, r.InferenceId);
            Assert.Equal(sessionId, r.SessionId);
        });
    }

    [Fact]
    public async Task Response_Completes_Within_Timeout()
    {
        var (sessionId, _) = await _fixture.AppClient.CreateAndClaimSessionAsync();
        var inferenceResp = await _fixture.AppClient.CreateInferenceAsync(sessionId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var responses = await _fixture.AppClient.SendInferenceAndCollectResponseAsync(
            sessionId, inferenceResp.InferenceId, "Hi", cts.Token);

        sw.Stop();
        Assert.NotEmpty(responses);
        Assert.True(sw.Elapsed.TotalSeconds < 60, $"Response took {sw.Elapsed.TotalSeconds}s");
    }
}
