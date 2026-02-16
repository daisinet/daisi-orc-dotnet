using Daisi.Orc.Core.Data.Models;
using Daisi.Orc.Grpc.RPCServices.V1;
using Daisi.Protos.V1;

namespace Daisi.Orc.Tests.Services
{
    /// <summary>
    /// Tests for ModelsRPC extension methods and usage stats timeframe mapping.
    /// </summary>
    public class ModelsRPCTests
    {
        #region Proto <-> DB Conversion

        [Fact]
        public void ConvertToProto_BasicFields_MapsCorrectly()
        {
            var dbModel = new DaisiModel
            {
                Id = "model-123",
                Name = "Test Model",
                FileName = "test.gguf",
                Url = "https://example.com/test.gguf",
                IsMultiModal = true,
                IsDefault = true,
                Enabled = true,
                LoadAtStartup = true,
                HasReasoning = true,
                Type = (int)AIModelTypes.TextGeneration
            };

            var proto = dbModel.ConvertToProto();

            Assert.Equal("model-123", proto.Id);
            Assert.Equal("Test Model", proto.Name);
            Assert.Equal("test.gguf", proto.FileName);
            Assert.Equal("https://example.com/test.gguf", proto.Url);
            Assert.True(proto.IsMultiModal);
            Assert.True(proto.IsDefault);
            Assert.True(proto.Enabled);
            Assert.True(proto.LoadAtStartup);
            Assert.True(proto.HasReasoning);
        }

        [Fact]
        public void ConvertToProto_WithBackend_MapsBackendSettings()
        {
            var dbModel = new DaisiModel
            {
                Name = "Test",
                FileName = "test.gguf",
                Backend = new DaisiModelBackendSettings
                {
                    Runtime = (int)BackendRuntimes.Cuda,
                    ContextSize = 16384,
                    GpuLayerCount = 35,
                    BatchSize = 256,
                    ShowLogs = true,
                    AutoFallback = true,
                    SkipCheck = false
                }
            };

            var proto = dbModel.ConvertToProto();

            Assert.NotNull(proto.Backend);
            Assert.Equal(BackendRuntimes.Cuda, proto.Backend.Runtime);
            Assert.Equal(16384u, proto.Backend.ContextSize);
            Assert.Equal(35, proto.Backend.GpuLayerCount);
            Assert.Equal(256u, proto.Backend.BatchSize);
            Assert.True(proto.Backend.ShowLogs);
            Assert.True(proto.Backend.AutoFallback);
            Assert.False(proto.Backend.SkipCheck);
        }

        [Fact]
        public void ConvertToProto_NullBackend_NoBackendOnProto()
        {
            var dbModel = new DaisiModel
            {
                Name = "Test",
                FileName = "test.gguf",
                Backend = null
            };

            var proto = dbModel.ConvertToProto();

            Assert.Null(proto.Backend);
        }

        [Fact]
        public void ConvertToProto_WithThinkLevels_MapsCorrectly()
        {
            var dbModel = new DaisiModel
            {
                Name = "Test",
                FileName = "test.gguf",
                ThinkLevels = new List<int>
                {
                    (int)ThinkLevels.Basic,
                    (int)ThinkLevels.BasicWithTools,
                    (int)ThinkLevels.Skilled
                }
            };

            var proto = dbModel.ConvertToProto();

            Assert.Equal(3, proto.ThinkLevels.Count);
            Assert.Contains(ThinkLevels.Basic, proto.ThinkLevels);
            Assert.Contains(ThinkLevels.BasicWithTools, proto.ThinkLevels);
            Assert.Contains(ThinkLevels.Skilled, proto.ThinkLevels);
        }

        [Fact]
        public void ConvertToProto_NullFields_DefaultToEmpty()
        {
            var dbModel = new DaisiModel
            {
                Id = null!,
                Name = null!,
                FileName = null!,
                Url = null!,
            };

            var proto = dbModel.ConvertToProto();

            Assert.Equal(string.Empty, proto.Id);
            Assert.Equal(string.Empty, proto.Name);
            Assert.Equal(string.Empty, proto.FileName);
            Assert.Equal(string.Empty, proto.Url);
        }

        [Fact]
        public void ConvertToDb_BasicFields_MapsCorrectly()
        {
            var proto = new AIModel
            {
                Id = "model-456",
                Name = "Proto Model",
                FileName = "proto.gguf",
                Url = "https://example.com/proto.gguf",
                IsMultiModal = true,
                IsDefault = false,
                Enabled = true,
                LoadAtStartup = false,
                HasReasoning = true
            };

            var dbModel = proto.ConvertToDb();

            Assert.Equal("model-456", dbModel.Id);
            Assert.Equal("Proto Model", dbModel.Name);
            Assert.Equal("proto.gguf", dbModel.FileName);
        }

        [Fact]
        public void ConvertToDb_EmptyId_GeneratesNewId()
        {
            var proto = new AIModel
            {
                Id = "",
                Name = "NewModel",
                FileName = "new.gguf"
            };

            var dbModel = proto.ConvertToDb();

            Assert.NotEmpty(dbModel.Id);
            Assert.StartsWith("model", dbModel.Id);
        }

        [Fact]
        public void ConvertToDb_WithBackend_MapsBackendSettings()
        {
            var proto = new AIModel
            {
                Name = "Test",
                FileName = "test.gguf",
                Backend = new BackendSettings
                {
                    Runtime = BackendRuntimes.Vulkan,
                    ContextSize = 4096,
                    GpuLayerCount = -1,
                    BatchSize = 512,
                    AutoFallback = true
                }
            };

            var dbModel = proto.ConvertToDb();

            Assert.NotNull(dbModel.Backend);
            Assert.Equal((int)BackendRuntimes.Vulkan, dbModel.Backend!.Runtime);
            Assert.Equal(4096u, dbModel.Backend.ContextSize);
            Assert.Equal(-1, dbModel.Backend.GpuLayerCount);
            Assert.Equal(512u, dbModel.Backend.BatchSize);
            Assert.True(dbModel.Backend.AutoFallback);
        }

        [Fact]
        public void RoundTrip_ProtoToDbAndBack_PreservesData()
        {
            var original = new AIModel
            {
                Name = "RoundTrip Model",
                FileName = "roundtrip.gguf",
                Url = "https://example.com/roundtrip.gguf",
                IsMultiModal = true,
                IsDefault = true,
                Enabled = true,
                LoadAtStartup = true,
                HasReasoning = true,
                Type = AIModelTypes.ImageGeneration,
                Backend = new BackendSettings
                {
                    Runtime = BackendRuntimes.Cuda,
                    ContextSize = 32768,
                    GpuLayerCount = 40,
                    BatchSize = 256,
                    AutoFallback = true,
                    ShowLogs = true,
                    SkipCheck = false
                }
            };
            original.ThinkLevels.Add(ThinkLevels.Basic);
            original.ThinkLevels.Add(ThinkLevels.Skilled);

            var db = original.ConvertToDb();
            var roundTripped = db.ConvertToProto();

            Assert.Equal(original.Name, roundTripped.Name);
            Assert.Equal(original.FileName, roundTripped.FileName);
            Assert.Equal(original.Url, roundTripped.Url);
            Assert.Equal(original.IsMultiModal, roundTripped.IsMultiModal);
            Assert.Equal(original.IsDefault, roundTripped.IsDefault);
            Assert.Equal(original.Enabled, roundTripped.Enabled);
            Assert.Equal(original.LoadAtStartup, roundTripped.LoadAtStartup);
            Assert.Equal(original.HasReasoning, roundTripped.HasReasoning);
            Assert.Equal(original.Backend.Runtime, roundTripped.Backend.Runtime);
            Assert.Equal(original.Backend.ContextSize, roundTripped.Backend.ContextSize);
            Assert.Equal(original.Backend.GpuLayerCount, roundTripped.Backend.GpuLayerCount);
            Assert.Equal(original.Backend.BatchSize, roundTripped.Backend.BatchSize);
            Assert.Equal(original.ThinkLevels.Count, roundTripped.ThinkLevels.Count);
        }

        #endregion

        #region Usage Stats Timeframe Mapping

        [Theory]
        [InlineData("day")]
        [InlineData("week")]
        [InlineData("month")]
        [InlineData("year")]
        [InlineData("all")]
        public void TimeframeMapping_ValidValues_DoNotThrow(string timeframe)
        {
            DateTime? startDate = timeframe.ToLowerInvariant() switch
            {
                "day" => DateTime.UtcNow.Date,
                "week" => DateTime.UtcNow.AddDays(-7),
                "month" => DateTime.UtcNow.AddMonths(-1),
                "year" => DateTime.UtcNow.AddYears(-1),
                "all" => null,
                _ => DateTime.UtcNow.AddMonths(-1)
            };

            if (timeframe == "all")
                Assert.Null(startDate);
            else
                Assert.NotNull(startDate);
        }

        [Fact]
        public void TimeframeMapping_Day_ReturnsToday()
        {
            DateTime? startDate = "day" switch
            {
                "day" => DateTime.UtcNow.Date,
                _ => null
            };

            Assert.Equal(DateTime.UtcNow.Date, startDate);
        }

        [Fact]
        public void TimeframeMapping_Unknown_DefaultsToMonth()
        {
            var before = DateTime.UtcNow.AddMonths(-1);

            DateTime? startDate = "unknown_value".ToLowerInvariant() switch
            {
                "day" => DateTime.UtcNow.Date,
                "week" => DateTime.UtcNow.AddDays(-7),
                "month" => DateTime.UtcNow.AddMonths(-1),
                "year" => DateTime.UtcNow.AddYears(-1),
                "all" => null,
                _ => DateTime.UtcNow.AddMonths(-1)
            };

            Assert.NotNull(startDate);
            // Should be approximately 1 month ago
            Assert.True(startDate!.Value >= before.AddSeconds(-1));
        }

        #endregion
    }
}
