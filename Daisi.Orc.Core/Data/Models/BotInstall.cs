using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class BotInstall
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.BotInstallIdPrefix);

        public string UserId { get; set; }

        public string UserName { get; set; }

        public string AccountId { get; set; }

        public string Version { get; set; }

        public string Platform { get; set; }

        public DateTime DateInstalled { get; set; } = DateTime.UtcNow;
    }
}
