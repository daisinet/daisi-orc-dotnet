namespace Daisi.Orc.Core.Data.Models.Marketplace;

/// <summary>
/// Cosmos DB data model for a secure tool setup parameter definition.
/// Describes a credential or configuration value that users must provide.
/// </summary>
public class SetupParameterData
{
    /// <summary>Parameter name (e.g. "apiKey", "region").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description shown to the user.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Input type hint: "text", "password", "apikey", "url", "json".</summary>
    public string Type { get; set; } = "text";

    /// <summary>Whether this parameter is required for the tool to function.</summary>
    public bool IsRequired { get; set; }
}
