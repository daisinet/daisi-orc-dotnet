namespace Daisi.Orc.Core.Data.Models.Marketplace;

/// <summary>
/// Cosmos DB data model for the tool definition embedded in a secure execution marketplace item.
/// Contains the metadata needed to present the tool to the inference engine.
/// </summary>
public class SecureToolDefinitionData
{
    /// <summary>Unique tool identifier (e.g. "weather-lookup").</summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Display name shown to the inference engine.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Instructions for the AI on when and how to use this tool.</summary>
    public string UseInstructions { get; set; } = string.Empty;

    /// <summary>Tool group for filtering (e.g. "InformationTools", "IntegrationTools").</summary>
    public string ToolGroup { get; set; } = string.Empty;

    /// <summary>The call parameters the AI provides at execution time.</summary>
    public List<SecureToolParameterData> Parameters { get; set; } = [];
}

/// <summary>
/// A single call parameter for a secure tool.
/// </summary>
public class SecureToolParameterData
{
    /// <summary>Parameter name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description to help the AI understand what to provide.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether the AI must always supply this parameter.</summary>
    public bool IsRequired { get; set; }
}
