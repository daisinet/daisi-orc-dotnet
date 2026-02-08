using System;
using System.Collections.Generic;
using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Daisi.Functions.CosmoDb
{
    public class HostUpdatesFunctions
    {
        private readonly ILogger _logger;
        Daisi.Orc.Core.Data.Db.Cosmo cosmo;
        public HostUpdatesFunctions(Daisi.Orc.Core.Data.Db.Cosmo cosmo, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<HostUpdatesFunctions>();
            this.cosmo = cosmo;
        }

        [Function("HostUpdated")]
        public async Task Run([CosmosDBTrigger(
            databaseName: "daisi",
            containerName: "Hosts",
            Connection = "Cosmo:ConnectionString",
            LeaseContainerName = "leases",
            CreateLeaseContainerIfNotExists = true)] IReadOnlyList<Host> input)
        {
            if (input != null && input.Count > 0)
            {
                foreach (var host in input.Where(h => h.UpdateOperation == "Web"))
                {
                    _logger.LogInformation("Updated by Web");
                    var keys = await cosmo.GetKeyStubsByOwnerIdAsync(host.Id);
                    foreach (var key in keys)
                    {
                        await cosmo.PatchKeyOwnerName(key, host.Name);
                    }

                    await cosmo.PatchHostForUpdateServiceAsync(host);
                }
            }
        }
    }


}
