using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class DaisiModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.ModelsIdPrefix);
        public string Name { get; set; }
        public string FileName { get; set; }
        public string Url { get; set; }
        public bool IsMultiModal { get; set; }
        public bool IsDefault { get; set; }
        public bool Enabled { get; set; }
        public bool LoadAtStartup { get; set; }
        public bool HasReasoning { get; set; }
        public List<int> ThinkLevels { get; set; } = new();
        public DaisiModelLLamaSettings? LLama { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class DaisiModelLLamaSettings
    {
        public int Runtime { get; set; }
        public uint ContextSize { get; set; }
        public int GpuLayerCount { get; set; }
        public uint BatchSize { get; set; }
        public bool ShowLogs { get; set; }
        public bool AutoFallback { get; set; }
        public bool SkipCheck { get; set; }
        public string? LlamaPath { get; set; }
        public string? LlavaPath { get; set; }
    }
}
