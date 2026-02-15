using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public enum AnomalyType
    {
        ReceiptReplay,
        InflatedTokens,
        ReceiptVolumeSpike,
        ZeroWorkUptime,
        CircularCreditFlow
    }

    public enum AnomalySeverity
    {
        Low,
        Medium,
        High
    }

    public enum AnomalyStatus
    {
        Open,
        Reviewed,
        Dismissed,
        ActionTaken
    }

    public class CreditAnomaly
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.CreditAnomalyIdPrefix);

        public string AccountId { get; set; }

        public string? HostId { get; set; }

        public AnomalyType Type { get; set; }

        public AnomalySeverity Severity { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// JSON blob with supporting data for the anomaly.
        /// </summary>
        public string? Details { get; set; }

        public AnomalyStatus Status { get; set; } = AnomalyStatus.Open;

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        public DateTime? DateReviewed { get; set; }

        public string? ReviewedBy { get; set; }
    }
}
