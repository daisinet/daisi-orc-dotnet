using Daisi.Orc.Core.Data.Db;
using Daisi.SDK.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class Account
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.AccountsIdPrefix);
        public string type { get; set; } = "Account";
        public string AccountId { get => Id; set => Id = value;  }
        public string Name { get; set; }
        public string TaxId { get; set;  }

        public List<Stub> Users { get; set; }
        public List<Stub> Hosts { get; set; } = new();
        public List<Dapp> Apps { get; set; } = new();
    }
}
