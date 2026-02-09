using Daisi.Orc.Core.Data.Db;
using Newtonsoft.Json;

namespace Daisi.Orc.Core.Data.Models
{
    public class CreditAccount
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.CreditAccountIdPrefix);

        public string AccountId { get; set; }

        /// <summary>
        /// Current credit balance. Should never go negative.
        /// </summary>
        public long Balance { get; set; }

        /// <summary>
        /// Lifetime credits earned from processing tokens.
        /// </summary>
        public long TotalEarned { get; set; }

        /// <summary>
        /// Lifetime credits spent on inference.
        /// </summary>
        public long TotalSpent { get; set; }

        /// <summary>
        /// Lifetime credits purchased.
        /// </summary>
        public long TotalPurchased { get; set; }

        /// <summary>
        /// Admin-configurable multiplier for token-based earnings.
        /// </summary>
        public double TokenEarnMultiplier { get; set; } = 1.0;

        /// <summary>
        /// Admin-configurable multiplier for uptime-based earnings.
        /// </summary>
        public double UptimeEarnMultiplier { get; set; } = 1.0;

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;

        public DateTime DateLastUpdated { get; set; } = DateTime.UtcNow;
    }
}
