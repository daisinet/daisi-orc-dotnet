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
        public int Type { get; set; }
        public List<int> Types { get; set; } = new();

        /// <summary>
        /// Backend-specific settings. Uses JsonPropertyName("lLama") for lazy CosmosDB migration
        /// so existing documents with "lLama" field name are deserialized correctly.
        /// </summary>
        [JsonPropertyName("lLama")]
        public DaisiModelBackendSettings? Backend { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class DaisiModelBackendSettings
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
        public string? BackendEngine { get; set; }
        public float? Temperature { get; set; }
        public float? TopP { get; set; }
        public int? TopK { get; set; }
        public float? RepeatPenalty { get; set; }
        public float? PresencePenalty { get; set; }
    }
}
