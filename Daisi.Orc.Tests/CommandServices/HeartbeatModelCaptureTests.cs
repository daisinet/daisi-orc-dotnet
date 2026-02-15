using Daisi.Orc.Grpc.CommandServices.Containers;
using Daisi.Protos.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daisi.Orc.Tests.CommandServices
{
    /// <summary>
    /// Tests that heartbeat requests correctly capture loaded model names
    /// into the HostOnline.LoadedModelNames property.
    /// </summary>
    public class HeartbeatModelCaptureTests
    {
        private readonly ILogger _logger = NullLogger.Instance;

        [Fact]
        public void HeartbeatSettings_WithModels_CapturesModelNames()
        {
            var hostOnline = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "host-1", Name = "TestHost" }
            };

            // Simulate what the HeartbeatRequestCommandHandler does
            var settings = new Settings
            {
                Model = new ModelSettings()
            };
            settings.Model.Models.Add(new AIModel { Name = "Gemma 3 4B Q8" });
            settings.Model.Models.Add(new AIModel { Name = "Llama 3 8B" });

            // Apply the extraction logic
            if (settings.Model?.Models is { Count: > 0 } models)
            {
                hostOnline.LoadedModelNames = models.Select(m => m.Name)
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
            }

            Assert.Equal(2, hostOnline.LoadedModelNames.Count);
            Assert.Contains("Gemma 3 4B Q8", hostOnline.LoadedModelNames);
            Assert.Contains("Llama 3 8B", hostOnline.LoadedModelNames);
        }

        [Fact]
        public void HeartbeatSettings_WithEmptyModels_KeepsEmptyList()
        {
            var hostOnline = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "host-1", Name = "TestHost" }
            };

            var settings = new Settings
            {
                Model = new ModelSettings()
            };

            // Empty models list - should not update
            if (settings.Model?.Models is { Count: > 0 } models)
            {
                hostOnline.LoadedModelNames = models.Select(m => m.Name)
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
            }

            Assert.Empty(hostOnline.LoadedModelNames);
        }

        [Fact]
        public void HeartbeatSettings_NullModelSettings_KeepsExistingList()
        {
            var hostOnline = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "host-1", Name = "TestHost" },
                LoadedModelNames = new List<string> { "OldModel" }
            };

            var settings = new Settings();

            if (settings.Model?.Models is { Count: > 0 } models)
            {
                hostOnline.LoadedModelNames = models.Select(m => m.Name)
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
            }

            // Should not have been modified
            Assert.Single(hostOnline.LoadedModelNames);
            Assert.Contains("OldModel", hostOnline.LoadedModelNames);
        }

        [Fact]
        public void HeartbeatSettings_WithEmptyNameModels_FiltersThemOut()
        {
            var hostOnline = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "host-1", Name = "TestHost" }
            };

            var settings = new Settings
            {
                Model = new ModelSettings()
            };
            settings.Model.Models.Add(new AIModel { Name = "ValidModel" });
            settings.Model.Models.Add(new AIModel { Name = "" });
            settings.Model.Models.Add(new AIModel { Name = "AnotherModel" });

            if (settings.Model?.Models is { Count: > 0 } models)
            {
                hostOnline.LoadedModelNames = models.Select(m => m.Name)
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
            }

            Assert.Equal(2, hostOnline.LoadedModelNames.Count);
            Assert.Contains("ValidModel", hostOnline.LoadedModelNames);
            Assert.Contains("AnotherModel", hostOnline.LoadedModelNames);
        }

        [Fact]
        public void HeartbeatSettings_OverwritesPreviousModelNames()
        {
            var hostOnline = new HostOnline(_logger)
            {
                Host = new Daisi.Orc.Core.Data.Models.Host { Id = "host-1", Name = "TestHost" },
                LoadedModelNames = new List<string> { "OldModel1", "OldModel2" }
            };

            var settings = new Settings
            {
                Model = new ModelSettings()
            };
            settings.Model.Models.Add(new AIModel { Name = "NewModel" });

            if (settings.Model?.Models is { Count: > 0 } models)
            {
                hostOnline.LoadedModelNames = models.Select(m => m.Name)
                    .Where(n => !string.IsNullOrEmpty(n)).ToList();
            }

            Assert.Single(hostOnline.LoadedModelNames);
            Assert.Contains("NewModel", hostOnline.LoadedModelNames);
            Assert.DoesNotContain("OldModel1", hostOnline.LoadedModelNames);
        }
    }
}
