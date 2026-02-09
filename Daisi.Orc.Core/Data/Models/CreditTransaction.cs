using Daisi.Orc.Core.Data.Db;
using Newtonsoft.Json;

namespace Daisi.Orc.Core.Data.Models
{
    public enum CreditTransactionType
    {
        TokenEarning,
        UptimeEarning,
        InferenceSpend,
        Purchase,
        AdminAdjustment
    }

    public class CreditTransaction
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.CreditTransactionIdPrefix);

        public string AccountId { get; set; }

        public CreditTransactionType Type { get; set; }

        /// <summary>
        /// Positive for credit, negative for debit.
        /// </summary>
        public long Amount { get; set; }

        /// <summary>
        /// Balance after this transaction.
        /// </summary>
        public long Balance { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// Related entity (InferenceId, HostId, etc.)
        /// </summary>
        public string? RelatedEntityId { get; set; }

        /// <summary>
        /// The multiplier that was applied at the time of this transaction.
        /// </summary>
        public double Multiplier { get; set; } = 1.0;

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    }
}
