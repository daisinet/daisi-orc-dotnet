using Daisi.Orc.Core.Data.Db;
using Grpc.Core;
using System.Security.Claims;

namespace Daisi.Orc.Grpc.Authentication
{
    public static class AuthenticationExtensions
    {
        public static string? GetClientKey(this ClaimsPrincipal user)
        {
            return user.Claims.FirstOrDefault(c => c.Type == DaisiAuthentication.CLIENT_KEY_CLAIM)?.Value;
        }
        public static string? GetAccountId(this ClaimsPrincipal user)
        {
            var accountId = user.Claims.FirstOrDefault(c => c.Type == DaisiAuthentication.ACCOUNT_ID_CLAIM)?.Value;
            return accountId;
        }
        public static string? GetUserId(this ClaimsPrincipal user)
        {
            var accountId = user.Claims.FirstOrDefault(c => c.Type == DaisiAuthentication.USER_ID_CLAIM)?.Value;
            return accountId;
        }
        public static string? GetAccountId(this ServerCallContext context)
        {
            var httpContext = context?.GetHttpContext();
            var user = httpContext?.User;
            return user?.GetAccountId();
        }
        public static string? GetUserId(this ServerCallContext context)
        {
            var httpContext = context?.GetHttpContext();
            var user = httpContext?.User;
            return user?.GetUserId();
        }
        public static string? GetClientKey(this ServerCallContext context)
        {
            return context?.GetHttpContext()?.User?.GetClientKey();
        }


        public static string? GetRemoteIpAddress(this ServerCallContext context)
        {
            var httpContext = context?.GetHttpContext();
            var connectionInfo = httpContext?.Connection;

            string? ipAddress = connectionInfo?.RemoteIpAddress?.MapToIPv4()?.ToString();
            if (ipAddress == "0.0.0.1" )
                ipAddress = context.GetLocalIpAddress();

            return ipAddress;
        }

        public static string? GetLocalIpAddress(this ServerCallContext context)
        {
            return "localhost";
        }
    }
}
