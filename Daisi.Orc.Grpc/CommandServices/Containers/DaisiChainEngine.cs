using Daisi.Protos.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Daisi.Orc.Grpc.CommandServices.Containers;

/// <summary>
/// DaisiChain: Orchestrates the token generation loop across a pipeline group.
/// For each token, sends activations through all pipeline stages sequentially,
/// receives logits from the final stage, samples the next token, and streams
/// the decoded text back to the caller.
/// </summary>
public class DaisiChainEngine
{
    private readonly PipelineGroupManager _groupManager;
    private readonly ILogger _logger;

    public DaisiChainEngine(PipelineGroupManager groupManager, ILogger logger)
    {
        _groupManager = groupManager;
        _logger = logger;
    }

    /// <summary>
    /// High-level entry point: tokenize text via the first-stage host, run inference,
    /// detokenize output tokens, and stream SendInferenceResponse objects.
    /// This is called by RelayInferenceRPC for pipeline sessions.
    /// </summary>
    public async IAsyncEnumerable<SendInferenceResponse> RunInferenceAsync(
        PipelineGroup group, SendInferenceRequest sendRequest,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Check pipeline health before starting
        if (group.Status == PipelineGroupStatus.Degraded)
        {
            _logger.LogError("DaisiChain: Pipeline group {GroupId} is degraded — cannot run inference", group.Id);
            yield return new SendInferenceResponse
            {
                SessionId = sendRequest.SessionId,
                InferenceId = sendRequest.InferenceId,
                Type = InferenceResponseTypes.Error,
                Content = "Pipeline is degraded — one or more hosts went offline.",
            };
            yield break;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Tokenize input via first-stage host
        var tokenizeResult = await _groupManager.TokenizeAsync(group, sendRequest.Text);
        if (tokenizeResult is null || tokenizeResult.TokenIds.Count == 0)
        {
            _logger.LogError("DaisiChain: Tokenization failed for group {GroupId}", group.Id);
            yield break;
        }

        var inputTokenIds = tokenizeResult.TokenIds.ToArray();
        var stopTokenIds = tokenizeResult.StopTokenIds.ToArray();

        var genParams = new GenerationParams
        {
            MaxTokens = sendRequest.HasMaxTokens ? sendRequest.MaxTokens : 256,
            Temperature = sendRequest.HasTemperature ? sendRequest.Temperature : 0.7f,
            TopK = sendRequest.HasTopK ? sendRequest.TopK : 40,
            TopP = sendRequest.HasTopP ? sendRequest.TopP : 0.9f,
            StopTokens = stopTokenIds,
        };

        int totalTokens = 0;
        await foreach (var tokenIdStr in GenerateAsync(group, sendRequest.SessionId,
            sendRequest.InferenceId, inputTokenIds, genParams, cancellationToken))
        {
            int tokenId = int.Parse(tokenIdStr);
            var text = await _groupManager.DetokenizeAsync(group, tokenId);

            totalTokens++;

            yield return new SendInferenceResponse
            {
                SessionId = sendRequest.SessionId,
                InferenceId = sendRequest.InferenceId,
                Type = InferenceResponseTypes.Text,
                Content = text ?? "",
                AuthorRole = "assistant",
                MessageTokenCount = totalTokens,
                ComputeTimeMs = (int)sw.ElapsedMilliseconds,
            };
        }
    }

    /// <summary>
    /// Run the inference loop for a pipeline session.
    /// Tokenizes input, processes each token through the pipeline stages,
    /// samples output tokens, and yields decoded text chunks.
    /// </summary>
    public async IAsyncEnumerable<string> GenerateAsync(
        PipelineGroup group, string sessionId, string inferenceId,
        int[] inputTokenIds, GenerationParams genParams,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stages = group.Stages.OrderBy(s => s.StageIndex).ToList();

        // Prefill: process all input tokens through the pipeline
        for (int i = 0; i < inputTokenIds.Length; i++)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            await ForwardTokenThroughPipeline(
                group, stages, sessionId, inferenceId,
                inputTokenIds[i], position: i, isPromptEnd: (i == inputTokenIds.Length - 1));
        }

        // Decode: generate tokens one at a time
        int position = inputTokenIds.Length;
        int lastTokenId = inputTokenIds[^1];

        for (int t = 0; t < genParams.MaxTokens; t++)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            // Forward the last generated token through the pipeline
            var logits = await ForwardTokenThroughPipeline(
                group, stages, sessionId, inferenceId,
                lastTokenId, position, isPromptEnd: false);

            if (logits is null)
            {
                _logger.LogError("DaisiChain: Pipeline returned null logits at position {Position}", position);
                yield break;
            }

            // Sample next token
            int nextTokenId = Sample(logits, genParams);

            // Check for EOS
            if (genParams.StopTokens is not null && genParams.StopTokens.Contains(nextTokenId))
                yield break;

            // Yield the token ID as a string (caller handles detokenization)
            yield return nextTokenId.ToString();

            lastTokenId = nextTokenId;
            position++;
        }
    }

    /// <summary>
    /// Forward a single token through all pipeline stages.
    /// P2P mode: send only to stage 0, which chains through all stages and returns logits.
    /// ORC-relay mode: send to each stage sequentially via the ORC command channel.
    /// Returns logits from the final stage, or null if any stage fails.
    /// </summary>
    private async Task<float[]?> ForwardTokenThroughPipeline(
        PipelineGroup group, List<PipelineStage> stages,
        string sessionId, string inferenceId,
        int tokenId, int position, bool isPromptEnd)
    {
        // P2P mode: send to stage 0 only — it chains through all stages via direct gRPC
        if (group.PeerRelayEnabled)
        {
            var request = new ForwardActivationRequest
            {
                PipelineGroupId = group.Id,
                SessionId = sessionId,
                InferenceId = inferenceId,
                StageIndex = 0,
                Position = position,
                TokenId = tokenId,
                IsPromptEnd = isPromptEnd,
            };

            var response = await _groupManager.ForwardActivationAsync(group, stages[0], request);
            if (response is null) return null;

            if (response.HasLogits)
                return DeserializeFloats(response.Logits);

            _logger.LogError("DaisiChain P2P: Stage 0 returned hidden state instead of logits — chain may be broken");
            return null;
        }

        // ORC-relay mode: send to each stage sequentially
        ByteString? currentHidden = null;

        for (int s = 0; s < stages.Count; s++)
        {
            var stage = stages[s];

            var request = new ForwardActivationRequest
            {
                PipelineGroupId = group.Id,
                SessionId = sessionId,
                InferenceId = inferenceId,
                StageIndex = stage.StageIndex,
                Position = position,
                IsPromptEnd = isPromptEnd && s == stages.Count - 1,
            };

            if (s == 0)
                request.TokenId = tokenId;
            else
                request.HiddenState = currentHidden!;

            var response = await _groupManager.ForwardActivationAsync(group, stage, request);
            if (response is null) return null;

            if (response.HasLogits)
                return DeserializeFloats(response.Logits);

            currentHidden = response.HiddenState;
        }

        _logger.LogError("DaisiChain: Pipeline completed without producing logits");
        return null;
    }

    /// <summary>
    /// Sample a token from logits using temperature + top-k + top-p.
    /// </summary>
    private static int Sample(float[] logits, GenerationParams p)
    {
        // Apply temperature
        if (p.Temperature > 0 && p.Temperature != 1.0f)
        {
            float invTemp = 1.0f / p.Temperature;
            for (int i = 0; i < logits.Length; i++)
                logits[i] *= invTemp;
        }

        // Greedy decode for temperature=0
        if (p.Temperature <= 0)
            return ArgMax(logits);

        // Softmax
        float maxLogit = logits.Max();
        float sumExp = 0;
        for (int i = 0; i < logits.Length; i++)
        {
            logits[i] = MathF.Exp(logits[i] - maxLogit);
            sumExp += logits[i];
        }
        for (int i = 0; i < logits.Length; i++)
            logits[i] /= sumExp;

        // Top-K filtering
        if (p.TopK > 0 && p.TopK < logits.Length)
        {
            var indexed = logits.Select((prob, idx) => (prob, idx))
                .OrderByDescending(x => x.prob)
                .ToArray();

            var allowed = new HashSet<int>(indexed.Take(p.TopK).Select(x => x.idx));
            for (int i = 0; i < logits.Length; i++)
                if (!allowed.Contains(i)) logits[i] = 0;

            // Renormalize
            float sum = logits.Sum();
            if (sum > 0)
                for (int i = 0; i < logits.Length; i++)
                    logits[i] /= sum;
        }

        // Top-P (nucleus) filtering
        if (p.TopP > 0 && p.TopP < 1.0f)
        {
            var sorted = logits.Select((prob, idx) => (prob, idx))
                .OrderByDescending(x => x.prob)
                .ToArray();

            float cumProb = 0;
            var allowed = new HashSet<int>();
            foreach (var (prob, idx) in sorted)
            {
                allowed.Add(idx);
                cumProb += prob;
                if (cumProb >= p.TopP) break;
            }

            for (int i = 0; i < logits.Length; i++)
                if (!allowed.Contains(i)) logits[i] = 0;

            float sum = logits.Sum();
            if (sum > 0)
                for (int i = 0; i < logits.Length; i++)
                    logits[i] /= sum;
        }

        // Weighted random sample
        float r = Random.Shared.NextSingle();
        float cumulative = 0;
        for (int i = 0; i < logits.Length; i++)
        {
            cumulative += logits[i];
            if (cumulative >= r) return i;
        }

        return ArgMax(logits);
    }

    private static int ArgMax(float[] arr)
    {
        int bestIdx = 0;
        float bestVal = arr[0];
        for (int i = 1; i < arr.Length; i++)
        {
            if (arr[i] > bestVal) { bestVal = arr[i]; bestIdx = i; }
        }
        return bestIdx;
    }

    private static float[] DeserializeFloats(ByteString bytes)
    {
        var data = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes.ToByteArray(), 0, data, 0, bytes.Length);
        return data;
    }
}

/// <summary>
/// Generation parameters for the DaisiChain inference loop.
/// </summary>
public class GenerationParams
{
    public int MaxTokens { get; init; } = 256;
    public float Temperature { get; init; } = 0.7f;
    public int TopK { get; init; } = 40;
    public float TopP { get; init; } = 0.9f;
    public int[]? StopTokens { get; init; }
}
