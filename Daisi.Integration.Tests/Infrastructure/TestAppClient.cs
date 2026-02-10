using Daisi.Protos.V1;
using Daisi.SDK.Models;
using Grpc.Core;
using Grpc.Net.Client;

namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Simulates an end-user app that creates sessions and runs inference via gRPC.
/// </summary>
public class TestAppClient
{
    private readonly GrpcChannel _channel;
    private readonly string _clientKey;
    private readonly SessionsProto.SessionsProtoClient _sessions;
    private readonly InferencesProto.InferencesProtoClient _inferences;

    public TestAppClient(GrpcChannel channel, string clientKey)
    {
        _channel = channel;
        _clientKey = clientKey;
        _sessions = new SessionsProto.SessionsProtoClient(channel);
        _inferences = new InferencesProto.InferencesProtoClient(channel);
    }

    private Metadata Headers => new()
    {
        { DaisiStaticSettings.ClientKeyHeader, _clientKey }
    };

    public async Task<CreateSessionResponse> CreateSessionAsync(string? hostId = null)
    {
        var request = new CreateSessionRequest();
        if (!string.IsNullOrEmpty(hostId))
            request.HostId = hostId;

        return await _sessions.CreateAsync(request, Headers);
    }

    public async Task<ClaimSessionResponse> ClaimSessionAsync(string sessionId)
    {
        return await _sessions.ClaimAsync(
            new ClaimSessionRequest { Id = sessionId }, Headers);
    }

    public async Task<ConnectResponse> ConnectSessionAsync(string sessionId)
    {
        return await _sessions.ConnectAsync(
            new ConnectRequest { SessionId = sessionId }, Headers);
    }

    public async Task<CloseSessionResponse> CloseSessionAsync(string sessionId)
    {
        return await _sessions.CloseAsync(
            new CloseSessionRequest { Id = sessionId }, Headers);
    }

    public async Task<(string sessionId, string hostId)> CreateAndClaimSessionAsync(bool connect = false)
    {
        var createResp = await CreateSessionAsync();
        await ClaimSessionAsync(createResp.Id);
        if (connect)
            await ConnectSessionAsync(createResp.Id);
        return (createResp.Id, createResp.Host.Id);
    }

    public async Task<CreateInferenceResponse> CreateInferenceAsync(
        string sessionId, string modelName = "Gemma 3 4B Q8 XL",
        ThinkLevels thinkLevel = ThinkLevels.Basic, string? initPrompt = null)
    {
        var request = new CreateInferenceRequest
        {
            SessionId = sessionId,
            ModelName = modelName,
            ThinkLevel = thinkLevel,
            InitializationPrompt = initPrompt ?? ""
        };

        return await _inferences.CreateAsync(request, Headers);
    }

    public async Task<List<SendInferenceResponse>> SendInferenceAndCollectResponseAsync(
        string sessionId, string inferenceId, string prompt, CancellationToken cancellationToken = default)
    {
        var request = new SendInferenceRequest
        {
            SessionId = sessionId,
            InferenceId = inferenceId,
            Text = prompt,
        };

        var responses = new List<SendInferenceResponse>();
        using var call = _inferences.Send(request, Headers, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            responses.Add(response);
        }

        return responses;
    }

    public async Task<InferenceStatsResponse> GetInferenceStatsAsync(
        string sessionId, string inferenceId)
    {
        return await _inferences.StatsAsync(
            new InferenceStatsRequest
            {
                SessionId = sessionId,
                InferenceId = inferenceId
            }, Headers);
    }

    public async Task<CloseInferenceResponse> CloseInferenceAsync(
        string sessionId, string inferenceId)
    {
        return await _inferences.CloseAsync(
            new CloseInferenceRequest
            {
                SessionId = sessionId,
                InferenceId = inferenceId,
                Reason = InferenceCloseReasons.CloseRequestedByClient
            }, Headers);
    }
}
