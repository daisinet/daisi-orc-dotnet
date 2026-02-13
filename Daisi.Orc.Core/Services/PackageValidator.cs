using System.IO.Compression;

namespace Daisi.Orc.Core.Services;

public class PackageValidationResult
{
    public bool IsValid { get; set; }
    public string? PackageType { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> SkillPaths { get; set; } = [];
    public List<string> ToolPaths { get; set; } = [];

    /// <summary>
    /// Path of the icon file found in the ZIP (e.g. "icon.png"), or null if none.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Raw bytes of the extracted icon file, ready to store publicly.
    /// </summary>
    public byte[]? IconBytes { get; set; }

    /// <summary>
    /// Content type of the icon (e.g. "image/png").
    /// </summary>
    public string? IconContentType { get; set; }
}

public class PackageValidator
{
    private const long MaxPackageSizeBytes = 100 * 1024 * 1024; // 100 MB
    private const long MaxSingleFileSizeBytes = 50 * 1024 * 1024; // 50 MB
    private const long MaxIconSizeBytes = 2 * 1024 * 1024; // 2 MB

    private static readonly Dictionary<string, string> IconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".png", "image/png" },
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".svg", "image/svg+xml" },
        { ".webp", "image/webp" }
    };

    public PackageValidationResult Validate(Stream zipStream)
    {
        var result = new PackageValidationResult();

        if (zipStream.Length > MaxPackageSizeBytes)
        {
            result.Errors.Add($"Package exceeds maximum size of {MaxPackageSizeBytes / (1024 * 1024)} MB");
            return result;
        }

        try
        {
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var entries = archive.Entries.ToList();

            if (entries.Count == 0)
            {
                result.Errors.Add("Package is empty");
                return result;
            }

            // Check for oversized files
            foreach (var entry in entries)
            {
                if (entry.Length > MaxSingleFileSizeBytes)
                {
                    result.Errors.Add($"File '{entry.FullName}' exceeds maximum size of {MaxSingleFileSizeBytes / (1024 * 1024)} MB");
                }
            }

            // Extract icon if present
            ExtractIcon(archive, entries, result);

            // Determine package type
            bool hasPluginMd = entries.Any(e => e.FullName.Equals("plugin.md", StringComparison.OrdinalIgnoreCase));
            bool hasSkillMd = entries.Any(e => e.FullName.Equals("skill.md", StringComparison.OrdinalIgnoreCase));
            bool hasToolMd = entries.Any(e => e.FullName.Equals("tool.md", StringComparison.OrdinalIgnoreCase));

            if (hasPluginMd)
            {
                result.PackageType = "Plugin";
                ValidatePluginPackage(entries, result);
            }
            else if (hasSkillMd)
            {
                result.PackageType = "Skill";
                ValidateSkillPackage(entries, result);
            }
            else if (hasToolMd)
            {
                result.PackageType = "Tool";
                ValidateToolPackage(entries, result);
            }
            else
            {
                result.Errors.Add("Package must contain a plugin.md, skill.md, or tool.md at the root");
            }

            result.IsValid = result.Errors.Count == 0;
        }
        catch (InvalidDataException)
        {
            result.Errors.Add("Invalid ZIP file format");
        }

        return result;
    }

    private static void ExtractIcon(ZipArchive archive, List<ZipArchiveEntry> entries, PackageValidationResult result)
    {
        // Look for icon file at the ZIP root (icon.png, icon.jpg, icon.svg, icon.webp)
        ZipArchiveEntry? iconEntry = null;
        string? contentType = null;

        foreach (var (ext, ct) in IconExtensions)
        {
            var candidate = entries.FirstOrDefault(e =>
                e.FullName.Equals($"icon{ext}", StringComparison.OrdinalIgnoreCase));
            if (candidate is not null)
            {
                iconEntry = candidate;
                contentType = ct;
                break;
            }
        }

        if (iconEntry is null)
        {
            result.Warnings.Add("No icon file found. Include an icon.png, icon.jpg, icon.svg, or icon.webp at the ZIP root for a marketplace listing image.");
            return;
        }

        if (iconEntry.Length > MaxIconSizeBytes)
        {
            result.Errors.Add($"Icon file exceeds maximum size of {MaxIconSizeBytes / (1024 * 1024)} MB. Please use a smaller image.");
            return;
        }

        // Extract icon bytes
        using var stream = iconEntry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        result.IconPath = iconEntry.FullName;
        result.IconBytes = ms.ToArray();
        result.IconContentType = contentType;
    }

    private static void ValidatePluginPackage(List<ZipArchiveEntry> entries, PackageValidationResult result)
    {
        // Check for skills/ and tools/ directories
        var skillEntries = entries.Where(e => e.FullName.StartsWith("skills/", StringComparison.OrdinalIgnoreCase)).ToList();
        var toolEntries = entries.Where(e => e.FullName.StartsWith("tools/", StringComparison.OrdinalIgnoreCase)).ToList();

        if (skillEntries.Count == 0 && toolEntries.Count == 0)
        {
            result.Warnings.Add("Plugin has no skills/ or tools/ directories");
        }

        // Validate skill entries have skill.md
        var skillDirs = skillEntries
            .Select(e => e.FullName.Split('/').Skip(1).FirstOrDefault())
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        foreach (var dir in skillDirs)
        {
            var skillMdPath = $"skills/{dir}/skill.md";
            if (!entries.Any(e => e.FullName.Equals(skillMdPath, StringComparison.OrdinalIgnoreCase)))
            {
                result.Warnings.Add($"Skill directory '{dir}' is missing skill.md");
            }
            else
            {
                result.SkillPaths.Add(skillMdPath);
            }
        }

        // Validate tool entries have tool.md and DLL
        var toolDirs = toolEntries
            .Select(e => e.FullName.Split('/').Skip(1).FirstOrDefault())
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .ToList();

        foreach (var dir in toolDirs)
        {
            var toolMdPath = $"tools/{dir}/tool.md";
            if (!entries.Any(e => e.FullName.Equals(toolMdPath, StringComparison.OrdinalIgnoreCase)))
            {
                result.Warnings.Add($"Tool directory '{dir}' is missing tool.md");
            }
            else
            {
                result.ToolPaths.Add(toolMdPath);
            }

            var hasDll = entries.Any(e =>
                e.FullName.StartsWith($"tools/{dir}/", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

            if (!hasDll)
            {
                result.Errors.Add($"Tool directory '{dir}' is missing a DLL file");
            }
        }
    }

    private static void ValidateSkillPackage(List<ZipArchiveEntry> entries, PackageValidationResult result)
    {
        result.SkillPaths.Add("skill.md");
    }

    private static void ValidateToolPackage(List<ZipArchiveEntry> entries, PackageValidationResult result)
    {
        result.ToolPaths.Add("tool.md");

        var hasDll = entries.Any(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        if (!hasDll)
        {
            result.Errors.Add("Tool package is missing a DLL file");
        }
    }
}
