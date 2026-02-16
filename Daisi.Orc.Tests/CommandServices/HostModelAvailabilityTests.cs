using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Threading.Channels;

namespace Daisi.Orc.Tests.CommandServices
{
    /// <summary>
    /// Tests that LoadedModelNames is correctly populated from heartbeat data
    /// and that host availability aggregation works correctly.
    /// </summary>
    public class HostModelAvailabilityTests : IDisposable
    {
        private readonly ILogger _logger = NullLogger.Instance;
        private readonly List<string> _hostIds = new();

        public void Dispose()
        {
            foreach (var hostId in _hostIds)
                HostContainer.HostsOnline.TryRemove(hostId, out _);
        }

        private HostOnline RegisterTestHost(string hostName, params string[] modelNames)
        {
            var hostId = $"hma-{Guid.NewGuid():N}";
            _hostIds.Add(hostId);

            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = hostId, Name = hostName },
                LoadedModelNames = modelNames.ToList()
            };
            HostContainer.HostsOnline.TryAdd(hostId, host);
            return host;
        }

        [Fact]
        public void LoadedModelNames_DefaultsToEmpty()
        {
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "test", Name = "TestHost" }
            };

            Assert.NotNull(host.LoadedModelNames);
            Assert.Empty(host.LoadedModelNames);
        }

        [Fact]
        public void LoadedModelNames_CanBeSetDirectly()
        {
            var host = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "test", Name = "TestHost" },
                LoadedModelNames = new List<string> { "Gemma 3 4B Q8", "Llama 3 8B" }
            };

            Assert.Equal(2, host.LoadedModelNames.Count);
            Assert.Contains("Gemma 3 4B Q8", host.LoadedModelNames);
            Assert.Contains("Llama 3 8B", host.LoadedModelNames);
        }

        [Fact]
        public void HostAvailability_SingleHostSingleModel_ReturnsOneEntry()
        {
            RegisterTestHost("Host-A", "Gemma 3 4B Q8");

            var result = AggregateHostAvailability();

            Assert.Single(result);
            Assert.Equal("Gemma 3 4B Q8", result[0].ModelName);
            Assert.Equal(1, result[0].HostCount);
            Assert.Contains("Host-A", result[0].HostNames);
        }

        [Fact]
        public void HostAvailability_MultipleHostsSameModel_AggregatesCorrectly()
        {
            RegisterTestHost("Host-A", "Gemma 3 4B Q8");
            RegisterTestHost("Host-B", "Gemma 3 4B Q8");
            RegisterTestHost("Host-C", "Gemma 3 4B Q8");

            var result = AggregateHostAvailability();

            Assert.Single(result);
            Assert.Equal(3, result[0].HostCount);
            Assert.Contains("Host-A", result[0].HostNames);
            Assert.Contains("Host-B", result[0].HostNames);
            Assert.Contains("Host-C", result[0].HostNames);
        }

        [Fact]
        public void HostAvailability_MultipleModels_GroupsCorrectly()
        {
            RegisterTestHost("Host-A", "Gemma 3 4B Q8", "Llama 3 8B");
            RegisterTestHost("Host-B", "Gemma 3 4B Q8");

            var result = AggregateHostAvailability();

            Assert.Equal(2, result.Count);

            var gemma = result.First(r => r.ModelName.Contains("Gemma", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, gemma.HostCount);

            var llama = result.First(r => r.ModelName.Contains("Llama", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, llama.HostCount);
            Assert.Contains("Host-A", llama.HostNames);
        }

        [Fact]
        public void HostAvailability_NoModelsLoaded_ReturnsEmpty()
        {
            RegisterTestHost("Host-A");

            var result = AggregateHostAvailability();

            Assert.Empty(result);
        }

        [Fact]
        public void HostAvailability_CaseInsensitiveGrouping()
        {
            RegisterTestHost("Host-A", "Gemma 3 4B Q8");
            RegisterTestHost("Host-B", "gemma 3 4b q8");

            var result = AggregateHostAvailability();

            // Both should be grouped together due to case-insensitive comparison
            Assert.Single(result);
            Assert.Equal(2, result[0].HostCount);
        }

        /// <summary>
        /// Reproduces the aggregation logic from ModelsRPC.GetModelHostAvailability
        /// to test it independently of gRPC plumbing.
        /// </summary>
        private List<ModelHostResult> AggregateHostAvailability()
        {
            var modelHosts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Filter to only our test hosts
            foreach (var hostId in _hostIds)
            {
                if (!HostContainer.HostsOnline.TryGetValue(hostId, out var hostOnline))
                    continue;

                foreach (var modelName in hostOnline.LoadedModelNames)
                {
                    if (!modelHosts.TryGetValue(modelName, out var hostNames))
                    {
                        hostNames = new List<string>();
                        modelHosts[modelName] = hostNames;
                    }
                    hostNames.Add(hostOnline.Host.Name);
                }
            }

            return modelHosts.Select(kvp => new ModelHostResult
            {
                ModelName = kvp.Key,
                HostCount = kvp.Value.Count,
                HostNames = kvp.Value
            }).ToList();
        }
    }

    internal class ModelHostResult
    {
        public string ModelName { get; set; } = "";
        public int HostCount { get; set; }
        public List<string> HostNames { get; set; } = new();
    }
}
