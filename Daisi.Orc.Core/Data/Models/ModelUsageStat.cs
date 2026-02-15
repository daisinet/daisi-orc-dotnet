namespace Daisi.Orc.Core.Data.Models
{
    public class ModelUsageStat
    {
        public string ModelName { get; set; } = "";
        public int InferenceCount { get; set; }
        public long TotalTokens { get; set; }
    }
}
