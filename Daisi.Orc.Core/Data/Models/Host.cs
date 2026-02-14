using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class Host
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.HostIdPrefex);
        public string AccountId { get; set; }
        public HostRegions Region { get; set; } = HostRegions.USSouthEast;
        public DateTime DateCreated { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public DateTime? DateStarted { get; set; }
        public DateTime? DateStopped { get; set; }
        public DateTime? DateLastSession { get; set; }
        public DateTime? DateLastHeartbeat { get; set; }
        public string OperatingSystem { get; set; }
        public string OperatingSystemVersion { get; set; }
        public HostStatus Status { get; set; }
        public bool DirectConnect { get; set; } = false;
        public bool PeerConnect { get; set; } = false;
        public string AppVersion { get; set; }
        public HostOrc? ConnectedOrc { get; set;  }
        public string UpdateOperation { get; set;  }
        public string? ReleaseGroup { get; set; }
        public string? SecretKeyId { get; set; }
    }
  
    public class HostOrc
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Domain { get; set; }
        public int Port { get; set; }
        
        public Stub[] Networks { get; set;  }
    }

    public enum HostRegions
    {
        /// <summary>
        /// Represents the United States Southeast geographic region.
        /// </summary>
        USSouthEast,
        /// <summary>
        /// Represents the United States Southwest geographic region.
        /// </summary>
        USSouthWest,
        /// <summary>
        /// Represents the United States Northeast geographic region.
        /// </summary>
        USNorthEast,
        /// <summary>
        /// Represents the United States Northwest geographic region.
        /// </summary>
        USNorthWest,
        /// <summary>
        /// Represents the Canada Eastern geographic region.
        /// </summary>
        CANEast,
        /// <summary>
        /// Represents the Canada Western geographic region.
        /// </summary>
        CANWest,
        /// <summary>
        /// Represents the United Kingdom (England, Scottland, Whales, and Northern Ireland) geographic region.
        /// </summary>
        UK,
        /// <summary>
        /// Represents the European Northwest geographic region.
        /// </summary>
        EURNorthWest,
        /// <summary>
        /// Represents the European Northeast geographic region.
        /// </summary>
        EURNorthEast,
        /// <summary>
        /// Represents the European Southwest geographic region.
        /// </summary>
        EURSouthWest,
        /// <summary>
        /// Represents the European Southeast geographic region.
        /// </summary>
        EURSouthEast,
        /// <summary>
        /// Represents the African Northwest geographic region.
        /// </summary>
        AFRNorthWest,
        /// <summary>
        /// Represents the African Northeast geographic region.
        /// </summary>
        AFRNorthEast,
        /// <summary>
        /// Represents the African Southwest geographic region.
        /// </summary>
        AFRSouthWest,
        /// <summary>
        /// Represents the African Southeast geographic region.
        /// </summary>
        AFRSouthEast,
        /// <summary>
        /// Represents the Middle East geographic region.
        /// </summary>
        MiddleEast,
        /// <summary>
        /// Represents the Central American geographic region.
        /// </summary>
        CentralAmerica,
        /// <summary>
        /// Represents the South American Northwest geographic region.
        /// </summary>
        SANorthWest,
        /// <summary>
        /// Represents the South American Northeast geographic region.
        /// </summary>
        SANorthEast,
        /// <summary>
        /// Represents the South American Southwest geographic region.
        /// </summary>
        SASouthWest,
        /// <summary>
        /// Represents the South American Southeast geographic region.
        /// </summary>
        SASouthEast,
        /// <summary>
        /// Represents the Asian Northwest geographic region.
        /// </summary>
        ASIANorthWest,
        /// <summary>
        /// Represents the Asian Northeast geographic region.
        /// </summary>
        ASIANorthEast,
        /// <summary>
        /// Represents the Asian Southwest geographic region.
        /// </summary>
        ASIASouthWest,
        /// <summary>
        /// Represents the Asian Southeast geographic region.
        /// </summary>
        ASIASouthEast,
        /// <summary>
        /// Represents the Australian and New Zealand geographic region.
        /// </summary>
        AUSNZ,
        /// <summary>
        /// Mobile Devices float between regions.
        /// </summary>
        Float

    }
}
