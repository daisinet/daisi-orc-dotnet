using Daisi.Orc.Core.Data.Db;
using Daisi.Protos.V1;
using Daisi.SDK.Extensions;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class User
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.UsersIdPrefix);
       
        [JsonPropertyName("type")]
        public string Type { get; set; } = "User";
        public string AccountId { get; set; }
        public string AccountName { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string? Source { get; set; }
        public UserRoles Role { get; set; }
        public UserStatus Status { get; set; } = UserStatus.Active;

        public DateTime? DateEmailConfirmed { get; set; }
        public DateTime? DatePhoneConfirmed { get; set; }
        public bool AllowedToLogin { get; set; } = true;
        public bool AllowSMS { get; set; } = true;
        public bool AllowEmail { get; set; } = true;

        public EmailList[] EmailLists { get; set; } = Array.Empty<EmailList>();

        public List<Stub> Hosts { get; set; } = new();

        public string? AuthCode { get; set; }
        public DateTime? DateAuthCodeSent { get; set; }

    }


}
