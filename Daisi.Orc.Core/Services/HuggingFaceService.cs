using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Daisi.Orc.Core.Services
{
    public class HuggingFaceService(IHttpClientFactory httpClientFactory, ILogger<HuggingFaceService> logger)
    {
        public async Task<HuggingFaceResult> LookupModelAsync(string repoUrl)
        {
            var repoId = ParseRepoId(repoUrl);
            if (string.IsNullOrEmpty(repoId))
                return HuggingFaceResult.Fail("Invalid HuggingFace URL. Expected format: https://huggingface.co/owner/repo");

            try
            {
                var client = httpClientFactory.CreateClient();
                var apiUrl = $"https://huggingface.co/api/models/{repoId}?blobs=true&expand[]=gguf";
                var response = await client.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                    return HuggingFaceResult.Fail($"HuggingFace API returned {response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var info = new HuggingFaceModelData
                {
                    RepoId = repoId,
                    ModelName = repoId.Contains('/') ? repoId.Split('/').Last() : repoId
                };

                if (root.TryGetProperty("pipeline_tag", out var pipelineTag))
                    info.PipelineTag = pipelineTag.GetString() ?? "";

                if (root.TryGetProperty("downloads", out var downloads))
                    info.Downloads = downloads.GetInt64();

                if (root.TryGetProperty("likes", out var likes))
                    info.Likes = likes.GetInt64();

                if (root.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tags.EnumerateArray())
                        info.Tags.Add(tag.GetString() ?? "");
                }

                // Parse GGUF metadata
                if (root.TryGetProperty("gguf", out var gguf))
                {
                    if (gguf.TryGetProperty("architecture", out var arch))
                        info.Architecture = arch.GetString() ?? "";

                    if (gguf.TryGetProperty("context_length", out var ctx))
                        info.ContextLength = (uint)ctx.GetInt64();
                }

                // Parse siblings for GGUF and ONNX files
                if (root.TryGetProperty("siblings", out var siblings) && siblings.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sibling in siblings.EnumerateArray())
                    {
                        if (!sibling.TryGetProperty("rfilename", out var rfilename))
                            continue;

                        var filename = rfilename.GetString() ?? "";

                        long size = 0;
                        if (sibling.TryGetProperty("size", out var sizeEl))
                            size = sizeEl.GetInt64();
                        else if (sibling.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("size", out var lfsSize))
                            size = lfsSize.GetInt64();

                        if (filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                        {
                            var quantType = ParseQuantType(filename);

                            info.GGUFFiles.Add(new HuggingFaceGGUFFileData
                            {
                                FileName = filename,
                                QuantType = quantType,
                                SizeBytes = size,
                                DownloadUrl = $"https://huggingface.co/{repoId}/resolve/main/{filename}?download=true"
                            });
                        }
                        else if (filename.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)
                            || filename.EndsWith(".onnx_data", StringComparison.OrdinalIgnoreCase)
                            || filename.EndsWith(".onnx.data", StringComparison.OrdinalIgnoreCase))
                        {
                            info.ONNXFiles.Add(new HuggingFaceONNXFileData
                            {
                                FileName = filename,
                                SizeBytes = size,
                                DownloadUrl = $"https://huggingface.co/{repoId}/resolve/main/{filename}?download=true"
                            });
                        }
                    }
                }

                return HuggingFaceResult.Ok(info);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error looking up HuggingFace model {RepoUrl}", repoUrl);
                return HuggingFaceResult.Fail($"Error: {ex.Message}");
            }
        }

        internal static string? ParseRepoId(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            url = url.Trim().TrimEnd('/');

            // Try to parse as URL
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.Trim('/').Split('/');
                if (segments.Length >= 2)
                    return $"{segments[0]}/{segments[1]}";
            }

            // Try as owner/repo format
            var parts = url.Split('/');
            if (parts.Length >= 2)
                return $"{parts[^2]}/{parts[^1]}";

            return null;
        }

        internal static string ParseQuantType(string filename)
        {
            // Match common quantization patterns in filenames
            var match = Regex.Match(filename,
                @"(?i)(IQ[0-9]_[A-Z0-9]+|Q[0-9]+_K(?:_[A-Z]+)?|Q[0-9]+_[0-9]+|BF16|F16|F32|FP16|FP32)",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Value.ToUpperInvariant() : "Unknown";
        }
    }

    public class HuggingFaceResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public HuggingFaceModelData? Model { get; set; }

        public static HuggingFaceResult Ok(HuggingFaceModelData model) => new() { Success = true, Model = model };
        public static HuggingFaceResult Fail(string error) => new() { Success = false, ErrorMessage = error };
    }

    public class HuggingFaceModelData
    {
        public string RepoId { get; set; } = "";
        public string ModelName { get; set; } = "";
        public string PipelineTag { get; set; } = "";
        public long Downloads { get; set; }
        public long Likes { get; set; }
        public List<string> Tags { get; set; } = new();
        public string Architecture { get; set; } = "";
        public uint ContextLength { get; set; }
        public List<HuggingFaceGGUFFileData> GGUFFiles { get; set; } = new();
        public List<HuggingFaceONNXFileData> ONNXFiles { get; set; } = new();
    }

    public class HuggingFaceGGUFFileData
    {
        public string FileName { get; set; } = "";
        public string QuantType { get; set; } = "";
        public long SizeBytes { get; set; }
        public string DownloadUrl { get; set; } = "";
    }

    public class HuggingFaceONNXFileData
    {
        public string FileName { get; set; } = "";
        public long SizeBytes { get; set; }
        public string DownloadUrl { get; set; } = "";
    }
}
