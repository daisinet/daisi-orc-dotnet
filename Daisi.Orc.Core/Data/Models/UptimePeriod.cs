using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public enum UptimeBonusTier
    {
        None,
        Bronze,
        Silver,
        Gold
    }

    public class UptimePeriod
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.UptimePeriodIdPrefix);

        public string AccountId { get; set; }

        public string HostId { get; set; }

        public DateTime DateStarted { get; set; }

        public DateTime? DateEnded { get; set; }

        public int TotalMinutes { get; set; }

        public long CreditsPaid { get; set; }

        public UptimeBonusTier BonusTier { get; set; } = UptimeBonusTier.None;
    }
}
