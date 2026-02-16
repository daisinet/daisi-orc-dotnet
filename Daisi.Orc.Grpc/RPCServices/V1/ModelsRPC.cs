using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class ModelsRPC(ILogger<ModelsRPC> logger, Cosmo cosmo, HuggingFaceService huggingFaceService) : ModelsProto.ModelsProtoBase
    {
        [HostsOnly]
        public async override Task<GetRequiredModelsResponse> GetRequiredModels(GetRequiredModelsRequest request, ServerCallContext context)
        {
            GetRequiredModelsResponse response = new();

            var models = await cosmo.GetEnabledModelsAsync();
            response.Models.AddRange(models.Select(m => m.ConvertToProto()));

            return response;
        }

        [Authorize]
        public async override Task<CreateModelResponse> CreateModel(CreateModelRequest request, ServerCallContext context)
        {
            var dbModel = request.Model.ConvertToDb();
            dbModel = await cosmo.CreateModelAsync(dbModel);

            return new CreateModelResponse { Model = dbModel.ConvertToProto() };
        }

        [Authorize]
        public async override Task<GetModelResponse> GetModel(GetModelRequest request, ServerCallContext context)
        {
            var model = await cosmo.GetModelAsync(request.Id);
            if (model == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Model not found."));

            return new GetModelResponse { Model = model.ConvertToProto() };
        }

        [Authorize]
        public async override Task<GetAllModelsResponse> GetAllModels(GetAllModelsRequest request, ServerCallContext context)
        {
            GetAllModelsResponse response = new();

            var models = await cosmo.GetAllModelsAsync();
            response.Models.AddRange(models.Select(m => m.ConvertToProto()));

            return response;
        }

        [Authorize]
        public async override Task<UpdateModelResponse> UpdateModel(UpdateModelRequest request, ServerCallContext context)
        {
            var existing = await cosmo.GetModelAsync(request.Model.Id);
            if (existing == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Model not found."));

            var dbModel = request.Model.ConvertToDb();
            dbModel.CreatedAt = existing.CreatedAt;
            dbModel = await cosmo.UpdateModelAsync(dbModel);

            return new UpdateModelResponse { Model = dbModel.ConvertToProto() };
        }

        [Authorize]
        public async override Task<DeleteModelResponse> DeleteModel(DeleteModelRequest request, ServerCallContext context)
        {
            var success = await cosmo.DeleteModelAsync(request.Id);
            return new DeleteModelResponse { Success = success };
        }

        [Authorize]
        public async override Task<LookupHuggingFaceModelResponse> LookupHuggingFaceModel(LookupHuggingFaceModelRequest request, ServerCallContext context)
        {
            var result = await huggingFaceService.LookupModelAsync(request.RepoUrl);

            var response = new LookupHuggingFaceModelResponse
            {
                Success = result.Success,
                ErrorMessage = result.ErrorMessage ?? ""
            };

            if (result.Success && result.Model is not null)
            {
                var info = new HuggingFaceModelInfo
                {
                    RepoId = result.Model.RepoId,
                    ModelName = result.Model.ModelName,
                    PipelineTag = result.Model.PipelineTag,
                    Downloads = result.Model.Downloads,
                    Likes = result.Model.Likes,
                    Architecture = result.Model.Architecture,
                    ContextLength = result.Model.ContextLength
                };
                info.Tags.AddRange(result.Model.Tags);

                foreach (var file in result.Model.GGUFFiles)
                {
                    info.GGUFFiles.Add(new HuggingFaceGGUFFile
                    {
                        FileName = file.FileName,
                        QuantType = file.QuantType,
                        SizeBytes = file.SizeBytes,
                        DownloadUrl = file.DownloadUrl
                    });
                }

                foreach (var file in result.Model.ONNXFiles)
                {
                    info.ONNXFiles.Add(new HuggingFaceONNXFile
                    {
                        FileName = file.FileName,
                        SizeBytes = file.SizeBytes,
                        DownloadUrl = file.DownloadUrl
                    });
                }

                response.Model = info;
            }

            return response;
        }

        [Authorize]
        public async override Task<GetModelUsageStatsResponse> GetModelUsageStats(GetModelUsageStatsRequest request, ServerCallContext context)
        {
            DateTime? startDate = request.Timeframe?.ToLowerInvariant() switch
            {
                "day" => DateTime.UtcNow.Date,
                "week" => DateTime.UtcNow.AddDays(-7),
                "month" => DateTime.UtcNow.AddMonths(-1),
                "year" => DateTime.UtcNow.AddYears(-1),
                "all" => null,
                _ => DateTime.UtcNow.AddMonths(-1)
            };

            var stats = await cosmo.GetModelUsageStatsAsync(startDate);

            var response = new GetModelUsageStatsResponse();
            foreach (var stat in stats)
            {
                response.Stats.Add(new ModelUsageStatProto
                {
                    ModelName = stat.ModelName,
                    InferenceCount = stat.InferenceCount,
                    TotalTokens = stat.TotalTokens
                });
            }

            return response;
        }

        [Authorize]
        public override Task<GetModelHostAvailabilityResponse> GetModelHostAvailability(GetModelHostAvailabilityRequest request, ServerCallContext context)
        {
            var response = new GetModelHostAvailabilityResponse();

            var modelHosts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var hostOnline in HostContainer.HostsOnline.Values)
            {
                foreach (var modelName in hostOnline.LoadedModelNames)
                {
                    if (!modelHosts.TryGetValue(modelName, out var hostNames))
                    {
                        hostNames = new List<string>();
                        modelHosts[modelName] = hostNames;
                    }
                    hostNames.Add(hostOnline.Host.Name);
                }
            }

            foreach (var (modelName, hostNames) in modelHosts)
            {
                var info = new ModelHostInfo
                {
                    ModelName = modelName,
                    HostCount = hostNames.Count
                };
                info.HostNames.AddRange(hostNames);
                response.Models.Add(info);
            }

            return Task.FromResult(response);
        }
    }

    public static class DaisiModelExtensions
    {
        extension(DaisiModel model)
        {
            public AIModel ConvertToProto()
            {
                var proto = new AIModel
                {
                    Id = model.Id ?? string.Empty,
                    Name = model.Name ?? string.Empty,
                    FileName = model.FileName ?? string.Empty,
                    Url = model.Url ?? string.Empty,
                    IsMultiModal = model.IsMultiModal,
                    IsDefault = model.IsDefault,
                    Enabled = model.Enabled,
                    LoadAtStartup = model.LoadAtStartup,
                    HasReasoning = model.HasReasoning,
                    Type = (AIModelTypes)model.Type
                };

                foreach (var level in model.ThinkLevels)
                {
                    proto.ThinkLevels.Add((ThinkLevels)level);
                }

                foreach (var type in model.Types)
                {
                    proto.Types_.Add((AIModelTypes)type);
                }

                // Backward compat: if Types is populated, set Type to the primary (first) type
                if (proto.Types_.Count > 0)
                    proto.Type = proto.Types_[0];

                if (model.Backend != null)
                {
                    proto.Backend = new BackendSettings
                    {
                        Runtime = (BackendRuntimes)model.Backend.Runtime,
                        ContextSize = model.Backend.ContextSize,
                        GpuLayerCount = model.Backend.GpuLayerCount,
                        BatchSize = model.Backend.BatchSize,
                        ShowLogs = model.Backend.ShowLogs,
                        AutoFallback = model.Backend.AutoFallback,
                        SkipCheck = model.Backend.SkipCheck,
                        LlamaPath = model.Backend.LlamaPath ?? string.Empty,
                        LlavaPath = model.Backend.LlavaPath ?? string.Empty,
                        BackendEngine = model.Backend.BackendEngine ?? string.Empty
                    };

                    if (model.Backend.Temperature.HasValue)
                        proto.Backend.Temperature = model.Backend.Temperature.Value;
                    if (model.Backend.TopP.HasValue)
                        proto.Backend.TopP = model.Backend.TopP.Value;
                    if (model.Backend.TopK.HasValue)
                        proto.Backend.TopK = model.Backend.TopK.Value;
                    if (model.Backend.RepeatPenalty.HasValue)
                        proto.Backend.RepeatPenalty = model.Backend.RepeatPenalty.Value;
                    if (model.Backend.PresencePenalty.HasValue)
                        proto.Backend.PresencePenalty = model.Backend.PresencePenalty.Value;
                }

                return proto;
            }
        }

        extension(AIModel proto)
        {
            public DaisiModel ConvertToDb()
            {
                var model = new DaisiModel
                {
                    Id = string.IsNullOrWhiteSpace(proto.Id) ? Cosmo.GenerateId(Cosmo.ModelsIdPrefix) : proto.Id,
                    Name = proto.Name,
                    FileName = proto.FileName,
                    Url = proto.Url,
                    IsMultiModal = proto.IsMultiModal,
                    IsDefault = proto.IsDefault,
                    Enabled = proto.Enabled,
                    LoadAtStartup = proto.LoadAtStartup,
                    HasReasoning = proto.HasReasoning,
                    ThinkLevels = proto.ThinkLevels.Select(t => (int)t).ToList(),
                    Type = (int)proto.Type,
                    Types = proto.Types_.Select(t => (int)t).ToList()
                };

                // Backward compat: if Types is empty but Type is set, populate Types from Type
                if (model.Types.Count == 0)
                    model.Types.Add(model.Type);

                if (proto.Backend != null)
                {
                    model.Backend = new DaisiModelBackendSettings
                    {
                        Runtime = (int)proto.Backend.Runtime,
                        ContextSize = proto.Backend.ContextSize,
                        GpuLayerCount = proto.Backend.GpuLayerCount,
                        BatchSize = proto.Backend.BatchSize,
                        ShowLogs = proto.Backend.ShowLogs,
                        AutoFallback = proto.Backend.AutoFallback,
                        SkipCheck = proto.Backend.SkipCheck,
                        LlamaPath = proto.Backend.LlamaPath,
                        LlavaPath = proto.Backend.LlavaPath,
                        BackendEngine = string.IsNullOrWhiteSpace(proto.Backend.BackendEngine) ? null : proto.Backend.BackendEngine,
                        Temperature = proto.Backend.HasTemperature ? proto.Backend.Temperature : null,
                        TopP = proto.Backend.HasTopP ? proto.Backend.TopP : null,
                        TopK = proto.Backend.HasTopK ? proto.Backend.TopK : null,
                        RepeatPenalty = proto.Backend.HasRepeatPenalty ? proto.Backend.RepeatPenalty : null,
                        PresencePenalty = proto.Backend.HasPresencePenalty ? proto.Backend.PresencePenalty : null
                    };
                }

                return model;
            }
        }
    }
}
