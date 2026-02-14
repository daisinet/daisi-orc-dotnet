using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class ModelsRPC(ILogger<ModelsRPC> logger, Cosmo cosmo) : ModelsProto.ModelsProtoBase
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
                    HasReasoning = model.HasReasoning
                };

                foreach (var level in model.ThinkLevels)
                {
                    proto.ThinkLevels.Add((ThinkLevels)level);
                }

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
                        LlavaPath = model.Backend.LlavaPath ?? string.Empty
                    };
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
                    ThinkLevels = proto.ThinkLevels.Select(t => (int)t).ToList()
                };

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
                        LlavaPath = proto.Backend.LlavaPath
                    };
                }

                return model;
            }
        }
    }
}
