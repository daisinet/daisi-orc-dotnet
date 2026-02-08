using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.SDK.Extensions;
using Newtonsoft.Json;

namespace Daisi.Orc.Core.Data.Models
{
    public class Blog
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Cosmo.GenerateId(Cosmo.BlogsIdPrefix);
        public string BlogId { get => Id; set => Id = value;  }
        public string Title { get; set; }
        public string Author { get; set;  }
        public string AuthorLink { get; set; }
        public Stub[] Tags { get; set;  }
        public string BodyMarkdown { get; set;  }
        public string ImageUrl { get; set; }
        public DateTime DateCreated { get; set; }
        public int LikeCount { get; set;  }
        public int ViewCount { get; set; }
    }

}
