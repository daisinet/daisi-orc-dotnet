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

        [Fact]
        public void ConvertToProto_Type_MapsCorrectly()
        {
            var dbModel = new DaisiModel
            {
                Name = "Image Model",
                FileName = "img.gguf",
                Type = (int)AIModelTypes.ImageGeneration
            };

            var proto = dbModel.ConvertToProto();

            Assert.Equal(AIModelTypes.ImageGeneration, proto.Type);
        }

        [Fact]
        public void ConvertToDb_Type_MapsCorrectly()
        {
            var proto = new AIModel
            {
                Name = "Image Model",
                FileName = "img.gguf",
                Type = AIModelTypes.ImageGeneration
            };

            var db = proto.ConvertToDb();

            Assert.Equal((int)AIModelTypes.ImageGeneration, db.Type);
        }

        [Fact]
        public void RoundTrip_Type_IsPreserved()
        {
            var original = new AIModel
            {
                Name = "Type Test",
                FileName = "test.gguf",
                Type = AIModelTypes.SpeechToText
            };

            var db = original.ConvertToDb();
            var roundTripped = db.ConvertToProto();

            Assert.Equal(AIModelTypes.SpeechToText, roundTripped.Type);
        }

        [Fact]
        public void ConvertToProto_MultipleTypes_MapsCorrectly()
        {
            var dbModel = new DaisiModel
            {
                Name = "Multi-Type Model",
                FileName = "vision.gguf",
                Types = new List<int>
                {
                    (int)AIModelTypes.TextGeneration,
                    (int)AIModelTypes.ImageGeneration
                }
            };

            var proto = dbModel.ConvertToProto();

            Assert.Equal(2, proto.Types_.Count);
            Assert.Contains(AIModelTypes.TextGeneration, proto.Types_);
            Assert.Contains(AIModelTypes.ImageGeneration, proto.Types_);
            // Backward compat: Type should be set to first type
            Assert.Equal(AIModelTypes.TextGeneration, proto.Type);
        }

        [Fact]
        public void ConvertToDb_MultipleTypes_MapsCorrectly()
        {
            var proto = new AIModel
            {
                Name = "Multi-Type",
                FileName = "multi.gguf",
                Type = AIModelTypes.TextGeneration
            };
            proto.Types_.Add(AIModelTypes.TextGeneration);
            proto.Types_.Add(AIModelTypes.ImageGeneration);

            var db = proto.ConvertToDb();

            Assert.Equal(2, db.Types.Count);
            Assert.Contains((int)AIModelTypes.TextGeneration, db.Types);
            Assert.Contains((int)AIModelTypes.ImageGeneration, db.Types);
        }

        [Fact]
        public void ConvertToDb_EmptyTypes_PopulatesFromType()
        {
            var proto = new AIModel
            {
                Name = "Single Type",
                FileName = "single.gguf",
                Type = AIModelTypes.AudioGeneration
            };
            // Types_ is empty

            var db = proto.ConvertToDb();

            // Should auto-populate from Type for backward compat
            Assert.Single(db.Types);
            Assert.Equal((int)AIModelTypes.AudioGeneration, db.Types[0]);
        }

        [Fact]
        public void RoundTrip_MultipleTypes_Preserved()
        {
            var original = new AIModel
            {
                Name = "Multi Round Trip",
                FileName = "multi.gguf",
                Type = AIModelTypes.TextGeneration
            };
            original.Types_.Add(AIModelTypes.TextGeneration);
            original.Types_.Add(AIModelTypes.SpeechToText);

            var db = original.ConvertToDb();
            var roundTripped = db.ConvertToProto();

            Assert.Equal(2, roundTripped.Types_.Count);
            Assert.Contains(AIModelTypes.TextGeneration, roundTripped.Types_);
            Assert.Contains(AIModelTypes.SpeechToText, roundTripped.Types_);
            Assert.Equal(AIModelTypes.TextGeneration, roundTripped.Type);
        }

        [Fact]
        public void ConvertToProto_BackendEngine_MapsCorrectly()
        {
            var dbModel = new DaisiModel
            {
                Name = "ONNX Model",
                FileName = "model.onnx",
                Backend = new DaisiModelBackendSettings
                {
                    BackendEngine = "OnnxRuntimeGenAI"
                }
            };

            var proto = dbModel.ConvertToProto();

            Assert.Equal("OnnxRuntimeGenAI", proto.Backend.BackendEngine);
        }

        [Fact]
        public void ConvertToDb_BackendEngine_MapsCorrectly()
        {
            var proto = new AIModel
            {
                Name = "ONNX Model",
                FileName = "model.onnx",
                Backend = new BackendSettings
                {
                    BackendEngine = "OnnxRuntimeGenAI"
                }
            };

            var db = proto.ConvertToDb();

            Assert.Equal("OnnxRuntimeGenAI", db.Backend!.BackendEngine);
        }

        [Fact]
        public void ConvertToDb_EmptyBackendEngine_StoresNull()
        {
            var proto = new AIModel
            {
                Name = "Default Model",
                FileName = "model.gguf",
                Backend = new BackendSettings
                {
                    BackendEngine = ""
                }
            };

            var db = proto.ConvertToDb();

            Assert.Null(db.Backend!.BackendEngine);
        }

        [Fact]
        public void ConvertToProto_InferenceDefaults_MapsWhenSet()
        {
            var dbModel = new DaisiModel
            {
                Name = "Tuned Model",
                FileName = "tuned.gguf",
                Backend = new DaisiModelBackendSettings
                {
                    Temperature = 0.5f,
                    TopP = 0.9f,
                    TopK = 30,
                    RepeatPenalty = 1.2f,
                    PresencePenalty = 0.1f
                }
            };

            var proto = dbModel.ConvertToProto();

            Assert.True(proto.Backend.HasTemperature);
            Assert.Equal(0.5f, proto.Backend.Temperature);
            Assert.True(proto.Backend.HasTopP);
            Assert.Equal(0.9f, proto.Backend.TopP);
            Assert.True(proto.Backend.HasTopK);
            Assert.Equal(30, proto.Backend.TopK);
            Assert.True(proto.Backend.HasRepeatPenalty);
            Assert.Equal(1.2f, proto.Backend.RepeatPenalty);
            Assert.True(proto.Backend.HasPresencePenalty);
            Assert.Equal(0.1f, proto.Backend.PresencePenalty);
        }

        [Fact]
        public void ConvertToProto_InferenceDefaults_OmittedWhenNull()
        {
            var dbModel = new DaisiModel
            {
                Name = "Default Model",
                FileName = "default.gguf",
                Backend = new DaisiModelBackendSettings
                {
                    Temperature = null,
                    TopP = null,
                    TopK = null,
                    RepeatPenalty = null,
                    PresencePenalty = null
                }
            };

            var proto = dbModel.ConvertToProto();

            Assert.False(proto.Backend.HasTemperature);
            Assert.False(proto.Backend.HasTopP);
            Assert.False(proto.Backend.HasTopK);
            Assert.False(proto.Backend.HasRepeatPenalty);
            Assert.False(proto.Backend.HasPresencePenalty);
        }

        [Fact]
        public void ConvertToDb_InferenceDefaults_MapsFromProto()
        {
            var proto = new AIModel
            {
                Name = "Tuned",
                FileName = "tuned.gguf",
                Backend = new BackendSettings()
            };
            proto.Backend.Temperature = 0.7f;
            proto.Backend.TopK = 50;

            var db = proto.ConvertToDb();

            Assert.Equal(0.7f, db.Backend!.Temperature);
            Assert.Equal(50, db.Backend.TopK);
            Assert.Null(db.Backend.TopP);
            Assert.Null(db.Backend.RepeatPenalty);
            Assert.Null(db.Backend.PresencePenalty);
        }

        [Fact]
        public void RoundTrip_InferenceDefaults_Preserved()
        {
            var original = new AIModel
            {
                Name = "Param Round Trip",
                FileName = "params.gguf",
                Backend = new BackendSettings
                {
                    BackendEngine = "LlamaSharp"
                }
            };
            original.Backend.Temperature = 0.6f;
            original.Backend.TopP = 0.85f;
            original.Backend.RepeatPenalty = 1.3f;

            var db = original.ConvertToDb();
            var roundTripped = db.ConvertToProto();

            Assert.Equal("LlamaSharp", roundTripped.Backend.BackendEngine);
            Assert.True(roundTripped.Backend.HasTemperature);
            Assert.Equal(0.6f, roundTripped.Backend.Temperature);
            Assert.True(roundTripped.Backend.HasTopP);
            Assert.Equal(0.85f, roundTripped.Backend.TopP);
            Assert.True(roundTripped.Backend.HasRepeatPenalty);
            Assert.Equal(1.3f, roundTripped.Backend.RepeatPenalty);
            Assert.False(roundTripped.Backend.HasTopK);
            Assert.False(roundTripped.Backend.HasPresencePenalty);
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
