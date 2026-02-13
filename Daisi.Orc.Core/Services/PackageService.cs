using System.IO.Compression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Daisi.Orc.Core.Services;

public class PackageService(IConfiguration configuration, ILogger<PackageService> logger)
{
    private readonly PackageValidator _validator = new();

    /// <summary>
    /// Parse and validate a package ZIP stream.
    /// </summary>
    public PackageValidationResult ParsePackage(Stream zipStream)
    {
        return _validator.Validate(zipStream);
    }

    /// <summary>
    /// Extract frontmatter from the manifest file (plugin.md, skill.md, or tool.md).
    /// </summary>
    public Dictionary<string, string>? ExtractManifest(Stream zipStream, string manifestPath = "plugin.md")
    {
        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry(manifestPath);
            if (entry is null)
                return null;

            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            return ParseYamlFrontmatter(content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract manifest from {ManifestPath}", manifestPath);
            return null;
        }
    }

    /// <summary>
    /// Store a package in Azure Blob Storage. Returns the blob URL.
    /// </summary>
    public async Task<string> StorePackageAsync(Stream zipStream, string itemId)
    {
        // V1: Azure Blob Storage
        var connectionString = configuration["Azure:BlobStorage:ConnectionString"];
        var containerName = configuration["Azure:BlobStorage:MarketplaceContainer"] ?? "marketplace-packages";

        if (string.IsNullOrEmpty(connectionString))
        {
            // Fallback for local development — store to local file system
            var localPath = Path.Combine(
                configuration["Daisi:DataPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "daisi-data"),
                "marketplace-packages");

            Directory.CreateDirectory(localPath);

            var filePath = Path.Combine(localPath, $"{itemId}.zip");
            zipStream.Position = 0;
            using var fileStream = File.Create(filePath);
            await zipStream.CopyToAsync(fileStream);

            logger.LogInformation("Stored package locally at {FilePath}", filePath);
            return $"file://{filePath}";
        }

        // Azure Blob Storage implementation
        // Uses Azure.Storage.Blobs when the package reference is available
        var blobName = $"{itemId}/{DateTime.UtcNow:yyyyMMddHHmmss}.zip";
        logger.LogInformation("Storing package to blob: {ContainerName}/{BlobName}", containerName, blobName);

        // TODO: Add Azure.Storage.Blobs package reference and implement blob upload
        // For now, fall back to local storage
        var fallbackPath = Path.Combine(
            configuration["Daisi:DataPath"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "daisi-data"),
            "marketplace-packages");
        Directory.CreateDirectory(fallbackPath);

        var fallbackFilePath = Path.Combine(fallbackPath, $"{itemId}.zip");
        zipStream.Position = 0;
        using var fs = File.Create(fallbackFilePath);
        await zipStream.CopyToAsync(fs);

        return $"file://{fallbackFilePath}";
    }

    /// <summary>
    /// Download a package from storage for host installation.
    /// </summary>
    public async Task<Stream?> DownloadPackageAsync(string packageBlobUrl)
    {
        if (string.IsNullOrEmpty(packageBlobUrl))
            return null;

        if (packageBlobUrl.StartsWith("file://"))
        {
            var filePath = packageBlobUrl[7..];
            if (!File.Exists(filePath))
                return null;

            var ms = new MemoryStream();
            using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }

        // TODO: Azure Blob Storage download
        logger.LogWarning("Blob storage download not yet implemented for URL: {Url}", packageBlobUrl);
        return null;
    }

    private static Dictionary<string, string> ParseYamlFrontmatter(string content)
    {
        var result = new Dictionary<string, string>();

        if (!content.StartsWith("---"))
            return result;

        var endIndex = content.IndexOf("---", 3);
        if (endIndex < 0)
            return result;

        var frontmatter = content[3..endIndex].Trim();
        foreach (var line in frontmatter.Split('\n'))
        {
            var trimmed = line.Trim();
            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0)
                continue;

            var key = trimmed[..colonIndex].Trim();
            var value = trimmed[(colonIndex + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }
}
