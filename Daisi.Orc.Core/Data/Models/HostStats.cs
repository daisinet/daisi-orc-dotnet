using Daisi.Protos.V1;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Data.Models
{
    public class HostStats
    {
        public string HostId { get; set; }
        public string NetworkId { get; set; }
        public long? TokenCount { get; set; }
        public long? SecondsProcessingTokens { get; set; }
        public Timeframe Timeframe { get; set;  }
        public DateTime Date { get; set;  }
        public int? Part { get; set; }
    }
}
