using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class Orchestrator
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.OrcsIdPrefix);
        public string Name { get; set; }
        public OrcStatus Status { get; set;  }

        public string AccountId { get; set; }
        public string AccountName { get; set; }

        public OrcNetwork[] Networks { get; set; } = [];

        public string Domain { get; set; }
        public int Port { get; set; }
        public bool RequiresSSL { get; set; }

        public int OpenConnectionCount { get; set; }
        public DateTime DateStart { get; set; }
        public DateTime DateStop { get; set; }

        public string Version { get; set; }
    }

    public class OrcNetwork
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsPublic { get; set;  }
    }
}
