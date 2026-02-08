using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Functions.CosmoDb
{
    public class DappUpdatesFunctions
    {
        private readonly ILogger _logger;
        Daisi.Orc.Core.Data.Db.Cosmo cosmo;
        public DappUpdatesFunctions(Daisi.Orc.Core.Data.Db.Cosmo cosmo, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<HostUpdatesFunctions>();
            this.cosmo = cosmo;
        }

        [Function("DappUpdated")]
        public async Task Run([CosmosDBTrigger(
            databaseName: "daisi",
            containerName: Cosmo.AppsContainerName,
            Connection = "Cosmo:ConnectionString",
            LeaseContainerName = "leases",
            CreateLeaseContainerIfNotExists = true)] IReadOnlyList<Dapp> input)
        {
            if (input != null && input.Count > 0)
            {
                foreach (var dapp in input)
                {
                    var keys = await cosmo.GetKeyStubsByOwnerIdAsync(dapp.Id);
                    foreach (var key in keys)
                    {
                        await cosmo.PatchKeyOwnerName(key, dapp.Name);
                    }

                }
            }
        }
    }
}
