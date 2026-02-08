using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1
{
    public class ModelsRPC(ILogger<ModelsRPC> logger) : ModelsProto.ModelsProtoBase
    {
        [HostsOnly]
        public async override Task<GetRequiredModelsResponse> GetRequiredModels(GetRequiredModelsRequest request, ServerCallContext context)
        {
            GetRequiredModelsResponse response = new();
           
            response.Models.Add(new AIModel() { LoadAtStartup = false, IsDefault = true, Enabled = true, IsMultiModal = false, Name = "Gemma 3 4B Q8 XL", FileName = "gemma-3-4b-it-UD-Q8_K_XL.gguf", Url = $"https://huggingface.co/unsloth/gemma-3-4b-it-GGUF/resolve/main/gemma-3-4b-it-UD-Q8_K_XL.gguf?download=true" });
            response.Models.Add(new AIModel() { LoadAtStartup = false, IsDefault = false, Enabled = true, IsMultiModal = false, Name = "Gemma 3 4B Q4 XL", FileName = "gemma-3-4b-it-UD-Q4_K_XL.gguf", Url = $"https://huggingface.co/unsloth/gemma-3-4b-it-GGUF/resolve/main/gemma-3-4b-it-UD-Q4_K_XL.gguf?download=true" });
            
            return response;
        }
    }
}
