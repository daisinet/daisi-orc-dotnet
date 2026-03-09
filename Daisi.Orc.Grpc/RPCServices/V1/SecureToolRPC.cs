using Daisi.Orc.Core.Services;
using Daisi.Orc.Grpc.Authentication;
using Daisi.Protos.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace Daisi.Orc.Grpc.RPCServices.V1;

/// <summary>
/// gRPC service for secure tool discovery. Returns installed tools with InstallId and
/// EndpointUrl so hosts can call providers directly. The ORC is not in the execution path.
/// </summary>
[Authorize]
public class SecureToolRPC(ILogger<SecureToolRPC> logger, SecureToolService secureToolService)
    : SecureToolProto.SecureToolProtoBase
{
    /// <summary>
    /// Get all installed secure tools for an account, including InstallId and EndpointUrl
    /// for direct host-to-provider communication.
    /// </summary>
    public override async Task<GetInstalledSecureToolsResponse> GetInstalledSecureTools(GetInstalledSecureToolsRequest request, ServerCallContext context)
    {
        var accountId = context.GetAccountId()
            ?? throw new RpcException(new Status(StatusCode.Unauthenticated, "Could not resolve account."));
        logger.LogInformation("SecureTool GetInstalled: AccountId={AccountId}", accountId);

        var tools = await secureToolService.GetInstalledToolsAsync(accountId);

        var response = new GetInstalledSecureToolsResponse();
        foreach (var installed in tools)
        {
            var info = new SecureToolDefinitionInfo
            {
                MarketplaceItemId = installed.MarketplaceItemId,
                ToolId = installed.Tool.ToolId,
                Name = installed.Tool.Name,
                UseInstructions = installed.Tool.UseInstructions,
                ToolGroup = installed.Tool.ToolGroup,
                InstallId = installed.InstallId,
                EndpointUrl = installed.EndpointUrl,
                BundleInstallId = installed.BundleInstallId
            };

            foreach (var p in installed.Tool.Parameters)
            {
                info.Parameters.Add(new SecureToolParameterInfo
                {
                    Name = p.Name,
                    Description = p.Description,
                    IsRequired = p.IsRequired
                });
            }

            response.Tools.Add(info);
        }

        return response;
    }
}
