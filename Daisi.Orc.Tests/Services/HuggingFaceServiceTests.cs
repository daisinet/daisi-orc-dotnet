using Daisi.Orc.Core.Services;

namespace Daisi.Orc.Tests.Services
{
    public class HuggingFaceServiceTests
    {
        #region ParseRepoId

        [Theory]
        [InlineData("https://huggingface.co/unsloth/gemma-3-4b-it-GGUF", "unsloth/gemma-3-4b-it-GGUF")]
        [InlineData("https://huggingface.co/unsloth/gemma-3-4b-it-GGUF/", "unsloth/gemma-3-4b-it-GGUF")]
        [InlineData("https://huggingface.co/TheBloke/Llama-2-7B-GGUF/tree/main", "TheBloke/Llama-2-7B-GGUF")]
        [InlineData("https://huggingface.co/TheBloke/Llama-2-7B-GGUF/blob/main/llama-2-7b.Q4_K_M.gguf", "TheBloke/Llama-2-7B-GGUF")]
        [InlineData("unsloth/gemma-3-4b-it-GGUF", "unsloth/gemma-3-4b-it-GGUF")]
        public void ParseRepoId_ValidInputs_ReturnsCorrectRepoId(string input, string expected)
        {
            var result = HuggingFaceService.ParseRepoId(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseRepoId_NullOrEmpty_ReturnsNull(string? input)
        {
            var result = HuggingFaceService.ParseRepoId(input!);
            Assert.Null(result);
        }

        [Fact]
        public void ParseRepoId_UrlWithTrailingWhitespace_TrimsAndParses()
        {
            var result = HuggingFaceService.ParseRepoId("  https://huggingface.co/owner/repo  ");
            Assert.Equal("owner/repo", result);
        }

        #endregion

        #region ParseQuantType

        [Theory]
        [InlineData("gemma-3-4b-it-UD-Q8_K_XL.gguf", "Q8_K_XL")]
        [InlineData("gemma-3-4b-it-UD-Q4_K_XL.gguf", "Q4_K_XL")]
        [InlineData("model-Q4_K_M.gguf", "Q4_K_M")]
        [InlineData("model-Q8_0.gguf", "Q8_0")]
        [InlineData("model-Q5_K_S.gguf", "Q5_K_S")]
        [InlineData("model-IQ2_XXS.gguf", "IQ2_XXS")]
        [InlineData("model-IQ3_XS.gguf", "IQ3_XS")]
        [InlineData("model-BF16.gguf", "BF16")]
        [InlineData("model-F16.gguf", "F16")]
        [InlineData("model-F32.gguf", "F32")]
        [InlineData("model-FP16.gguf", "FP16")]
        [InlineData("model-FP32.gguf", "FP32")]
        public void ParseQuantType_KnownPatterns_ReturnsCorrectType(string filename, string expected)
        {
            var result = HuggingFaceService.ParseQuantType(filename);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ParseQuantType_NoMatch_ReturnsUnknown()
        {
            var result = HuggingFaceService.ParseQuantType("some-model.gguf");
            Assert.Equal("Unknown", result);
        }

        [Fact]
        public void ParseQuantType_CaseInsensitive_ReturnsUpperCase()
        {
            var result = HuggingFaceService.ParseQuantType("model-q4_k_m.gguf");
            Assert.Equal("Q4_K_M", result);
        }

        #endregion

        #region LookupModelAsync

        [Fact]
        public async Task LookupModelAsync_InvalidUrl_ReturnsFailure()
        {
            var httpClientFactory = new TestHttpClientFactory(null!);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("");

            Assert.False(result.Success);
            Assert.Contains("Invalid", result.ErrorMessage);
        }

        [Fact]
        public async Task LookupModelAsync_ApiReturns404_ReturnsFailure()
        {
            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.NotFound, "{}");
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/nonexistent");

            Assert.False(result.Success);
            Assert.Contains("NotFound", result.ErrorMessage!);
        }

        [Fact]
        public async Task LookupModelAsync_ValidResponse_ParsesMetadata()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 50000,
                "likes": 1200,
                "tags": ["text-generation", "gguf", "llama"],
                "gguf": {
                    "architecture": "llama",
                    "context_length": 8192
                },
                "siblings": [
                    { "rfilename": "model-Q4_K_M.gguf", "size": 4200000000 },
                    { "rfilename": "model-Q8_0.gguf", "size": 7800000000 },
                    { "rfilename": "README.md", "size": 1024 },
                    { "rfilename": "config.json", "size": 512 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/TheBloke/TestModel-GGUF");

            Assert.True(result.Success);
            Assert.NotNull(result.Model);

            Assert.Equal("TheBloke/TestModel-GGUF", result.Model!.RepoId);
            Assert.Equal("TestModel-GGUF", result.Model.ModelName);
            Assert.Equal("text-generation", result.Model.PipelineTag);
            Assert.Equal(50000, result.Model.Downloads);
            Assert.Equal(1200, result.Model.Likes);
            Assert.Equal("llama", result.Model.Architecture);
            Assert.Equal(8192u, result.Model.ContextLength);

            Assert.Contains("text-generation", result.Model.Tags);
            Assert.Contains("gguf", result.Model.Tags);
        }

        [Fact]
        public async Task LookupModelAsync_ValidResponse_FiltersGGUFFiles()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": [],
                "siblings": [
                    { "rfilename": "model-Q4_K_M.gguf", "size": 4200000000 },
                    { "rfilename": "model-Q8_0.gguf", "size": 7800000000 },
                    { "rfilename": "README.md", "size": 1024 },
                    { "rfilename": "config.json", "size": 512 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/repo");

            Assert.Equal(2, result.Model!.GGUFFiles.Count);
            Assert.All(result.Model.GGUFFiles, f => Assert.EndsWith(".gguf", f.FileName));
        }

        [Fact]
        public async Task LookupModelAsync_ValidResponse_BuildsCorrectDownloadUrls()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": [],
                "siblings": [
                    { "rfilename": "model-Q4_K_M.gguf", "size": 4200000000 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/repo");

            var file = result.Model!.GGUFFiles[0];
            Assert.Equal("model-Q4_K_M.gguf", file.FileName);
            Assert.Equal("Q4_K_M", file.QuantType);
            Assert.Equal(4200000000, file.SizeBytes);
            Assert.Equal("https://huggingface.co/owner/repo/resolve/main/model-Q4_K_M.gguf?download=true", file.DownloadUrl);
        }

        [Fact]
        public async Task LookupModelAsync_LfsSize_UsesLfsSize()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": [],
                "siblings": [
                    { "rfilename": "model-Q4_K_M.gguf", "lfs": { "size": 9999999 } }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/repo");

            Assert.Equal(9999999, result.Model!.GGUFFiles[0].SizeBytes);
        }

        [Fact]
        public async Task LookupModelAsync_NoGGUFMetadata_StillReturnsFiles()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": [],
                "siblings": [
                    { "rfilename": "model.gguf", "size": 1000 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/repo");

            Assert.True(result.Success);
            Assert.Equal("", result.Model!.Architecture);
            Assert.Equal(0u, result.Model.ContextLength);
            Assert.Single(result.Model.GGUFFiles);
        }

        [Fact]
        public async Task LookupModelAsync_ValidResponse_DetectsONNXFiles()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": ["onnx"],
                "siblings": [
                    { "rfilename": "model.onnx", "size": 5000000000 },
                    { "rfilename": "onnx/model.onnx", "size": 3000000000 },
                    { "rfilename": "onnx/model.onnx_data", "size": 2000000000 },
                    { "rfilename": "cuda/model.onnx.data", "size": 1500000000 },
                    { "rfilename": "model-Q4_K_M.gguf", "size": 4200000000 },
                    { "rfilename": "README.md", "size": 1024 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/onnx-repo");

            Assert.True(result.Success);
            Assert.Equal(1, result.Model!.GGUFFiles.Count);
            Assert.Equal(4, result.Model.ONNXFiles.Count);
        }

        [Fact]
        public async Task LookupModelAsync_ONNXFiles_BuildsCorrectDownloadUrls()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": [],
                "siblings": [
                    { "rfilename": "onnx/model.onnx", "size": 3000000000 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/repo");

            var onnxFile = result.Model!.ONNXFiles[0];
            Assert.Equal("onnx/model.onnx", onnxFile.FileName);
            Assert.Equal(3000000000, onnxFile.SizeBytes);
            Assert.Equal("https://huggingface.co/owner/repo/resolve/main/onnx/model.onnx?download=true", onnxFile.DownloadUrl);
        }

        [Fact]
        public async Task LookupModelAsync_NoONNXFiles_ReturnsEmptyList()
        {
            var json = """
            {
                "pipeline_tag": "text-generation",
                "downloads": 100,
                "likes": 10,
                "tags": [],
                "siblings": [
                    { "rfilename": "model-Q4_K_M.gguf", "size": 4200000000 },
                    { "rfilename": "config.json", "size": 512 }
                ]
            }
            """;

            var handler = new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, json);
            var httpClientFactory = new TestHttpClientFactory(handler);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<HuggingFaceService>.Instance;
            var service = new HuggingFaceService(httpClientFactory, logger);

            var result = await service.LookupModelAsync("https://huggingface.co/owner/repo");

            Assert.True(result.Success);
            Assert.Empty(result.Model!.ONNXFiles);
        }

        #endregion
    }

    #region Test Helpers

    internal class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly System.Net.HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public FakeHttpMessageHandler(System.Net.HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    internal class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler? _handler;

        public TestHttpClientFactory(HttpMessageHandler? handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return _handler is not null ? new HttpClient(_handler) : new HttpClient();
        }
    }

    #endregion
}
