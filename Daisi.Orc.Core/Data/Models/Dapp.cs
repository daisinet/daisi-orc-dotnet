using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class Dapp
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.AppsIdPrefix);
        public string Name { get; set; }
        public string SecretKeyId { get; set; }
        public bool IsDaisiApp { get; set; }
        public string AccountId { get; set; }
        public string AccountName { get; set; }
        public DappTypes Type { get; set; }
        public DateTime? DateApproved { get; set; }
    }
}
 