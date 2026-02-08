using Daisi.Protos.V1;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Daisi.Orc.Core.Data.Models
{
    public class AccessKeyOwner
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public SystemRoles SystemRole { get; set; }
        public string AccountId { get; set; }
    }
}
