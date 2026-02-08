using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;

namespace Daisi.Orc.Core.Services
{
    public class OrcService(IConfiguration configuration, Cosmo cosmo)
    {



        public string? AccountId => configuration[$"Daisi:{nameof(AccountId)}"];
        public string? MinimumHostVersion => configuration[$"Daisi:{nameof(MinimumHostVersion)}"];
        public string? MinimumHostVersion_Beta => configuration[$"Daisi:MinimumHostVersion-beta"];
        public int? MaxHosts {
            get {
                var maxHostsString = configuration[$"Daisi:{nameof(MaxHosts)}"];
                if (!string.IsNullOrWhiteSpace(maxHostsString))
                {
                   return int.Parse(maxHostsString);
                }
                return 20;
            }
        }
        public string? NextOrcId => configuration[$"Daisi:{nameof(NextOrcId)}"];
        public string? OrcId => configuration[$"Daisi:{nameof(OrcId)}"];


        public async Task<HostOrc> GetHostOrcAsync()
        {
            var orc = (await cosmo.GetOrcsAsync(null, OrcId, AccountId)).Items.FirstOrDefault();
            if (orc == null) return null;

            return new HostOrc()
            {
                Domain = orc.Domain,
                Id = orc.Id,
                Name = orc.Name,
                Port = orc.Port,
            };
        }
    }
}
