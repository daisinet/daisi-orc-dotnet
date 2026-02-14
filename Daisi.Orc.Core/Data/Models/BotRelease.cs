using Daisi.Orc.Core.Data.Db;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class BotRelease
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.BotReleaseIdPrefix);

        public string ReleaseGroup { get; set; }

        public string Version { get; set; }

        public string? SemVer { get; set; }

        public string? TuiDownloadUrl { get; set; }

        public string? MauiDownloadUrl { get; set; }

        public bool IsActive { get; set; }

        public string? ReleaseNotes { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    }
}
