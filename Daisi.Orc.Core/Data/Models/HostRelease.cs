using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class HostRelease
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.ReleaseIdPrefix);

        public string ReleaseGroup { get; set; }

        public string Version { get; set; }

        public string DownloadUrl { get; set; }

        public bool IsActive { get; set; }

        public string? ReleaseNotes { get; set; }

        public string? RequiredOrcVersion { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    }
}
