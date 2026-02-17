using Daisi.Orc.Core.Data.Models.Marketplace;

namespace Daisi.Orc.Tests.Models;

/// <summary>
/// Smoke tests verifying the new OAuth fields on SetupParameterData.
/// </summary>
public class SetupParameterDataTests
{
    [Fact]
    public void DefaultValues_OAuthFieldsAreEmpty()
    {
        var param = new SetupParameterData();

        Assert.Equal(string.Empty, param.AuthUrl);
        Assert.Equal(string.Empty, param.ServiceLabel);
    }

    [Fact]
    public void OAuthFields_SetAndRetrieve()
    {
        var param = new SetupParameterData
        {
            Name = "office365",
            Description = "Connect to Office 365",
            Type = "oauth",
            IsRequired = true,
            AuthUrl = "https://provider.com/api/auth/start",
            ServiceLabel = "Office 365"
        };

        Assert.Equal("oauth", param.Type);
        Assert.Equal("https://provider.com/api/auth/start", param.AuthUrl);
        Assert.Equal("Office 365", param.ServiceLabel);
    }

    [Fact]
    public void NonOAuthParam_OAuthFieldsIgnored()
    {
        var param = new SetupParameterData
        {
            Name = "apiKey",
            Type = "apikey",
            AuthUrl = string.Empty,
            ServiceLabel = string.Empty
        };

        Assert.Equal("apikey", param.Type);
        Assert.Equal(string.Empty, param.AuthUrl);
        Assert.Equal(string.Empty, param.ServiceLabel);
    }
}
