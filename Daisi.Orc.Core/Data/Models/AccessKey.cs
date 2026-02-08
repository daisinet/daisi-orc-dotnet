using Daisi.Orc.Core.Data.Db;
using Daisi.SDK.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class AccessKey
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.AccessKeyIdPrefix);

        [JsonProperty(PropertyName = "type")]
        public string Type { get; set; }

        ///// <summary>
        ///// Cosmo listens to this field when determining Time To Live. Must be lowercase.
        ///// </summary>
        //[JsonProperty(PropertyName = "ttl")]
        //public int TimeToLive { get; set; } = 24 * 60 * 60;
        
        [JsonProperty(PropertyName = "key")]
        public string Key { get; set; }

        /// <summary>
        /// Gets or sets the owner of the key. For secret keys this could be an App, Orc, or a Host.
        /// For client keys, it could be any of those, but also includes Accounts and Users.
        /// </summary>
        public AccessKeyOwner Owner { get; set; }
      
        /// <summary>
        /// The Date that the token expires. Key is automatically deleted after 30 days
        /// by the database, so DateExpires cannot be more than 30 days from date of creation.
        /// </summary>
        public DateTime? DateExpires { get; set; } //=> TimeToLive == -1 ? null : DateTime.UtcNow.AddSeconds(TimeToLive);
        public string IpAddress { get; set; }
        public string MachineName { get; set; }

        /// <summary>
        /// Client keys are created from supplying this secret key.
        /// </summary>
        public string? ParentKeyId { get; set; }

        /// <summary>
        /// These are the IDs for the resources that this key has access to.
        /// </summary>
        public List<string> AccessToIDs { get; set; } = new();

    }

    public record KeyTypes(string Name)
    {
        /// <summary>
        /// Private keys that should never be sent or shared with anyone other than an Orc
        /// </summary>
        public static KeyTypes Secret { get; } = new KeyTypes("secret");
        /// <summary>
        /// Represents a client that interacts with a service or resource.
        /// </summary>
        public static KeyTypes Client { get; } = new KeyTypes("client");
    }

    //public enum SystemRoles
    //{
    //    /// <summary>
    //    /// Hosts are systems that process service requests.
    //    /// </summary>
    //    Host,
    //    /// <summary>
    //    /// Apps are consumers of the services provided by the hosts.
    //    /// </summary>
    //    App,
    //    /// <summary>
    //    /// Orc coordinate the requests make by apps with the hosts.
    //    /// </summary>
    //    Orc,
    //    /// <summary>
    //    /// Represents a user within the system, including identifying information and relevant user attributes.
    //    /// </summary>
    //    /// <remarks>Use this class to manage user-related data, such as authentication details, profile
    //    /// information, and permissions. Instances of this class may be used to track active users, manage access
    //    /// control, or store user preferences.</remarks>
    //    User
    //}
}
