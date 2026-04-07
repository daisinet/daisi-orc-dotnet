using System.Collections.Concurrent;
using Daisi.Protos.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Daisi.Orc.Grpc.CommandServices.Containers;

/// <summary>
/// DaisiChain: Manages pipeline groups — sets of hosts that collectively serve a single
/// model via pipeline parallelism. Each group splits the model's transformer layers across
/// multiple hosts, where each host computes its assigned layer range and passes activations
/// to the next host in the pipeline.
/// </summary>
public class PipelineGroupManager
{
    private readonly ConcurrentDictionary<string, PipelineGroup> _groups = new();
    private readonly ILogger _logger;

    public PipelineGroupManager(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create a pipeline group by assigning layer ranges to available hosts.
    /// Splits layers evenly across hosts. First host gets embedding, last gets output head.
    /// </summary>
    public PipelineGroup CreateGroup(
        string modelName, int totalLayers, int hiddenDim, int vocabSize,
        List<HostOnline> availableHosts)
    {
        if (availableHosts.Count < 2)
            throw new InvalidOperationException("Pipeline parallelism requires at least 2 hosts.");

        var groupId = $"pipeline-{Guid.NewGuid():N}";
        int numHosts = availableHosts.Count;
        int layersPerHost = totalLayers / numHosts;
        int remainder = totalLayers % numHosts;

        // Check if all hosts support P2P
        bool allPeerConnect = availableHosts.All(h => h.Host.PeerConnect);

        var stages = new List<PipelineStage>();
        int currentLayer = 0;

        for (int i = 0; i < numHosts; i++)
        {
            int layerCount = layersPerHost + (i < remainder ? 1 : 0);
            int startLayer = currentLayer;
            int endLayer = currentLayer + layerCount;

            var stage = new PipelineStage
            {
                StageIndex = i,
                HostId = availableHosts[i].Host.Id,
                StartLayer = startLayer,
                EndLayer = endLayer,
                IncludeEmbedding = (i == 0),
                IncludeOutputHead = (i == numHosts - 1),
                PeerConnectEnabled = allPeerConnect,
            };

            // P2P: tell each stage (except the last) where the next stage lives
            if (allPeerConnect && i < numHosts - 1)
            {
                var nextHost = availableHosts[i + 1];
                stage.NextStageHostIp = nextHost.Host.IpAddress ?? "";
                stage.NextStageHostPort = 4242; // peer gRPC port
            }

            stages.Add(stage);
            currentLayer = endLayer;
        }

        var group = new PipelineGroup
        {
            Id = groupId,
            ModelName = modelName,
            TotalLayers = totalLayers,
            HiddenDim = hiddenDim,
            VocabSize = vocabSize,
            Status = PipelineGroupStatus.Pending,
            PeerRelayEnabled = allPeerConnect,
        };
        group.Stages.AddRange(stages);

        _groups[groupId] = group;

        _logger.LogInformation("DaisiChain: Created pipeline group {GroupId} for model '{Model}' with {Stages} stages across {Layers} layers",
            groupId, modelName, numHosts, totalLayers);

        return group;
    }

    /// <summary>
    /// Send LoadPipelineStageRequest to each host in the group and wait for all to be ready.
    /// </summary>
    public async Task<bool> LoadGroupAsync(PipelineGroup group, string modelFileName, string modelUrl,
        uint contextSize = 4096, bool shardsAvailable = false)
    {
        var tasks = new List<Task<bool>>();

        foreach (var stage in group.Stages)
        {
            tasks.Add(LoadStageOnHostAsync(group.Id, stage, modelFileName, modelUrl, contextSize,
                group.PeerRelayEnabled, shardsAvailable));
        }

        var results = await Task.WhenAll(tasks);
        bool allSucceeded = results.All(r => r);

        group.Status = allSucceeded
            ? PipelineGroupStatus.Ready
            : PipelineGroupStatus.Degraded;

        if (allSucceeded)
            _logger.LogInformation("DaisiChain: Pipeline group {GroupId} is ready", group.Id);
        else
            _logger.LogError("DaisiChain: Pipeline group {GroupId} failed to load on all hosts", group.Id);

        return allSucceeded;
    }

    private async Task<bool> LoadStageOnHostAsync(string groupId, PipelineStage stage,
        string modelFileName, string modelUrl, uint contextSize, bool peerRelayEnabled,
        bool shardsAvailable = false)
    {
        if (!HostContainer.HostsOnline.TryGetValue(stage.HostId, out var hostOnline))
        {
            _logger.LogError("DaisiChain: Host {HostId} not online for pipeline stage {Stage}", stage.HostId, stage.StageIndex);
            return false;
        }

        var request = new LoadPipelineStageRequest
        {
            PipelineGroupId = groupId,
            Stage = stage,
            ModelFileName = modelFileName,
            ModelUrl = modelUrl,
            ContextSize = contextSize,
            PeerRelayEnabled = peerRelayEnabled,
        };

        // Shard-based partial download: compute which shard files this stage needs
        if (shardsAvailable)
        {
            request.UseShardedDownload = true;
            request.ModelBaseUrl = modelUrl;

            // Header is always needed (metadata + tensor info for all stages)
            request.ShardFileNames.Add($"{modelFileName}.header");

            // First stage needs embedding
            if (stage.IncludeEmbedding)
                request.ShardFileNames.Add($"{modelFileName}.embed");

            // Last stage needs output head
            if (stage.IncludeOutputHead)
                request.ShardFileNames.Add($"{modelFileName}.output");

            // Layer shards for this stage's assigned range
            for (int i = stage.StartLayer; i < stage.EndLayer; i++)
                request.ShardFileNames.Add($"{modelFileName}.layer.{i}");

            _logger.LogInformation("DaisiChain: Stage {Stage} will download {Count} shard files ({Files})",
                stage.StageIndex, request.ShardFileNames.Count,
                string.Join(", ", request.ShardFileNames.Take(5)) + (request.ShardFileNames.Count > 5 ? "..." : ""));
        }

        try
        {
            var requestId = $"req-{Random.Shared.Next(10000, 99999)}";
            var requestChannel = System.Threading.Channels.Channel.CreateUnbounded<Command>();
            hostOnline.RequestChannels[requestId] = requestChannel;

            // Send command to host via its outgoing queue
            hostOnline.OutgoingQueue.Writer.TryWrite(new Command
            {
                Name = nameof(LoadPipelineStageRequest),
                RequestId = requestId,
                Payload = Any.Pack(request),
            });

            // Wait for response with 60s timeout
            using var cts = new CancellationTokenSource(60000);
            await foreach (var response in requestChannel.Reader.ReadAllAsync(cts.Token))
            {
                hostOnline.RequestChannels.TryRemove(requestId, out _);
                if (response.Payload.TryUnpack<LoadPipelineStageResponse>(out var loadResponse))
                {
                    if (!loadResponse.Success)
                        _logger.LogError("DaisiChain: Host {HostId} failed to load stage: {Error}", stage.HostId, loadResponse.ErrorMessage);
                    return loadResponse.Success;
                }
                return false;
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("DaisiChain: Timeout waiting for host {HostId} to load pipeline stage", stage.HostId);
            return false;
        }
    }

    /// <summary>
    /// Send a forward activation request to a specific pipeline stage host and wait for the response.
    /// </summary>
    public async Task<ForwardActivationResponse?> ForwardActivationAsync(
        PipelineGroup group, PipelineStage stage, ForwardActivationRequest request,
        int timeoutMs = 30000)
    {
        if (!HostContainer.HostsOnline.TryGetValue(stage.HostId, out var hostOnline))
        {
            _logger.LogError("DaisiChain: Host {HostId} not online for activation relay", stage.HostId);
            return null;
        }

        var requestId = $"req-{Random.Shared.Next(10000, 99999)}";
        var requestChannel = System.Threading.Channels.Channel.CreateUnbounded<Command>();
        hostOnline.RequestChannels[requestId] = requestChannel;

        hostOnline.OutgoingQueue.Writer.TryWrite(new Command
        {
            Name = nameof(ForwardActivationRequest),
            RequestId = requestId,
            Payload = Any.Pack(request),
        });

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await foreach (var response in requestChannel.Reader.ReadAllAsync(cts.Token))
            {
                hostOnline.RequestChannels.TryRemove(requestId, out _);
                if (response.Payload.TryUnpack<ForwardActivationResponse>(out var activationResponse))
                    return activationResponse;
                return null;
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            hostOnline.RequestChannels.TryRemove(requestId, out _);
            _logger.LogError("DaisiChain: Timeout waiting for activation from host {HostId}", stage.HostId);
            return null;
        }
    }

    public PipelineGroup? GetGroup(string groupId) =>
        _groups.TryGetValue(groupId, out var group) ? group : null;

    public void RemoveGroup(string groupId) =>
        _groups.TryRemove(groupId, out _);

    /// <summary>
    /// Ask the first-stage host (which has the tokenizer) to tokenize text.
    /// </summary>
    public async Task<PipelineTokenizeResponse?> TokenizeAsync(PipelineGroup group, string text, int timeoutMs = 10000)
    {
        var firstStage = group.Stages.OrderBy(s => s.StageIndex).First();
        if (!HostContainer.HostsOnline.TryGetValue(firstStage.HostId, out var hostOnline))
            return null;

        var requestId = $"req-{Random.Shared.Next(10000, 99999)}";
        var requestChannel = System.Threading.Channels.Channel.CreateUnbounded<Command>();
        hostOnline.RequestChannels[requestId] = requestChannel;

        hostOnline.OutgoingQueue.Writer.TryWrite(new Command
        {
            Name = nameof(PipelineTokenizeRequest),
            RequestId = requestId,
            Payload = Any.Pack(new PipelineTokenizeRequest
            {
                PipelineGroupId = group.Id,
                Text = text,
            }),
        });

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await foreach (var response in requestChannel.Reader.ReadAllAsync(cts.Token))
            {
                hostOnline.RequestChannels.TryRemove(requestId, out _);
                if (response.Payload.TryUnpack<PipelineTokenizeResponse>(out var tokenizeResponse))
                    return tokenizeResponse;
                return null;
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            hostOnline.RequestChannels.TryRemove(requestId, out _);
            return null;
        }
    }

    /// <summary>
    /// Ask the first-stage host to detokenize a single token ID.
    /// </summary>
    public async Task<string?> DetokenizeAsync(PipelineGroup group, int tokenId, int timeoutMs = 5000)
    {
        var firstStage = group.Stages.OrderBy(s => s.StageIndex).First();
        if (!HostContainer.HostsOnline.TryGetValue(firstStage.HostId, out var hostOnline))
            return null;

        var requestId = $"req-{Random.Shared.Next(10000, 99999)}";
        var requestChannel = System.Threading.Channels.Channel.CreateUnbounded<Command>();
        hostOnline.RequestChannels[requestId] = requestChannel;

        hostOnline.OutgoingQueue.Writer.TryWrite(new Command
        {
            Name = nameof(PipelineDetokenizeRequest),
            RequestId = requestId,
            Payload = Any.Pack(new PipelineDetokenizeRequest
            {
                PipelineGroupId = group.Id,
                TokenId = tokenId,
            }),
        });

        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await foreach (var response in requestChannel.Reader.ReadAllAsync(cts.Token))
            {
                hostOnline.RequestChannels.TryRemove(requestId, out _);
                if (response.Payload.TryUnpack<PipelineDetokenizeResponse>(out var detokenizeResponse))
                    return detokenizeResponse.Text;
                return null;
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            hostOnline.RequestChannels.TryRemove(requestId, out _);
            return null;
        }
    }

    /// <summary>
    /// Called when a host goes offline. Marks any pipeline groups containing that host as Degraded.
    /// </summary>
    public void OnHostOffline(string hostId)
    {
        foreach (var group in _groups.Values)
        {
            if (group.Status == PipelineGroupStatus.Degraded)
                continue;

            bool affected = group.Stages.Any(s => s.HostId == hostId);
            if (affected)
            {
                group.Status = PipelineGroupStatus.Degraded;
                _logger.LogWarning("DaisiChain: Pipeline group {GroupId} degraded — host {HostId} went offline",
                    group.Id, hostId);
            }
        }
    }

    /// <summary>
    /// Send PipelineResetStateRequest to all stage hosts in a group.
    /// Called when a pipeline session is closed.
    /// </summary>
    public void ResetGroupSessionState(string groupId, string sessionId)
    {
        if (!_groups.TryGetValue(groupId, out var group))
            return;

        foreach (var stage in group.Stages)
        {
            if (!HostContainer.HostsOnline.TryGetValue(stage.HostId, out var hostOnline))
                continue;

            hostOnline.OutgoingQueue.Writer.TryWrite(new Command
            {
                Name = nameof(PipelineResetStateRequest),
                Payload = Any.Pack(new PipelineResetStateRequest
                {
                    PipelineGroupId = groupId,
                    SessionId = sessionId,
                }),
            });
        }
    }

    /// <summary>
    /// Send UnloadPipelineStageRequest to all stage hosts and remove the group.
    /// Called when a pipeline model is disabled/deleted.
    /// </summary>
    public void UnloadGroup(string groupId)
    {
        if (!_groups.TryRemove(groupId, out var group))
            return;

        foreach (var stage in group.Stages)
        {
            if (!HostContainer.HostsOnline.TryGetValue(stage.HostId, out var hostOnline))
                continue;

            hostOnline.OutgoingQueue.Writer.TryWrite(new Command
            {
                Name = nameof(UnloadPipelineStageRequest),
                Payload = Any.Pack(new UnloadPipelineStageRequest
                {
                    PipelineGroupId = groupId,
                }),
            });
        }

        _logger.LogInformation("DaisiChain: Unloaded pipeline group {GroupId} for model '{Model}'",
            groupId, group.ModelName);
    }

    /// <summary>
    /// Unload all pipeline groups for a given model name.
    /// Called when a model is deleted/disabled.
    /// </summary>
    public void UnloadGroupsForModel(string modelName)
    {
        var groupIds = _groups.Values
            .Where(g => string.Equals(g.ModelName, modelName, StringComparison.OrdinalIgnoreCase))
            .Select(g => g.Id)
            .ToList();

        foreach (var groupId in groupIds)
            UnloadGroup(groupId);
    }

    /// <summary>Check if a pipeline group is healthy (all hosts online).</summary>
    public bool IsGroupHealthy(string groupId)
    {
        if (!_groups.TryGetValue(groupId, out var group))
            return false;

        return group.Status != PipelineGroupStatus.Degraded
            && group.Stages.All(s => HostContainer.HostsOnline.ContainsKey(s.HostId));
    }

    public IReadOnlyCollection<PipelineGroup> GetAllGroups() =>
        _groups.Values.ToList().AsReadOnly();
}
