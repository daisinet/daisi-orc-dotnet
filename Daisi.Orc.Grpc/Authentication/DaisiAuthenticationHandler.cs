using Daisi.Orc.Core.Data.Db;
using Daisi.Orc.Core.Data.Models;
using Daisi.Protos.V1;
using Daisi.SDK.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace Daisi.Orc.Grpc.Authentication
{
    public class DaisiAuthentication
    {
        public const string USER_ID_CLAIM = "UserId";
        public const string CLIENT_KEY_CLAIM = "ClientKey";
        public const string ACCOUNT_ID_CLAIM = "AccountId";
        public const string KEY_TYPE_CLAIM = "KeyType"; 

    }
    public class DaisiAuthenticationOptions : AuthenticationSchemeOptions
    {
        public const string DefaultScheme = "DaisiAuthenticationScheme";
        public string TokenHeaderName { get; set; } = DaisiStaticSettings.ClientKeyHeader;
    }
    public class DaisiAuthenticationHandler : AuthenticationHandler<DaisiAuthenticationOptions>
    {


        public DaisiAuthenticationHandler(IOptionsMonitor<DaisiAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder)
        {

        }

        protected async override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            //Console.WriteLine("DaisiAuthenticationHandler: HandleAuthenticateAsync called.");
            var endpoint = Context.GetEndpoint();
            if (endpoint is null || endpoint.Metadata.Any(i => i is AllowAnonymousAttribute))
            {
                Logger.LogInformation($"DaisiAuthenticationHandler: AllowAnonymous found on {endpoint.DisplayName}");
                return AuthenticateResult.NoResult();
            }

            if (!Request.Headers.TryGetValue(Options.TokenHeaderName, out StringValues clientKeyValues) || !clientKeyValues.Any())
                return AuthenticateResult.Fail($"DAISI: No key found in header.");

            var claims = new List<Claim>();
            var key = clientKeyValues.First()!;

            KeyTypes type = key.StartsWith("secret-") ? KeyTypes.Secret : KeyTypes.Client;

            // Validate that the client key exists in the db.
            var cosmo = Context.RequestServices.GetService<Cosmo>();
            if (cosmo is null)
                return AuthenticateResult.Fail($"DAISI: NO DB ACCESS.");

            var result = await cosmo.GetKeyAsync(key, type);
            if (result is null)
                return AuthenticateResult.Fail($"DAISI: KEY NOT FOUND");

            if (result.Type == KeyTypes.Secret.Name)
                return AuthenticateResult.Fail($"DAISI: WRONG KEY TYPE");

            // Check for expiration.
            if (result.DateExpires.HasValue && result.DateExpires.Value < DateTime.UtcNow)
                return AuthenticateResult.Fail($"DAISI: EXPIRED KEY");

            // Add appropriate claims depending on type of client
            if (endpoint.Metadata.Any(i => i is HostsOnlyAttribute) && result.Owner.SystemRole != SystemRoles.HostDevice)
                return AuthenticateResult.Fail($"DAISI: HOSTS ONLY");

      
            var claimsIdentity = new ClaimsIdentity(claims, Scheme.Name);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            claimsIdentity.AddClaim(new Claim(DaisiAuthentication.KEY_TYPE_CLAIM, type.ToString()));
            claimsIdentity.AddClaim(new Claim(DaisiAuthentication.CLIENT_KEY_CLAIM, key));

            if (result.Owner?.SystemRole == SystemRoles.User)
            {
                var allowedLogin = await cosmo.UserAllowedToLogin(result.Owner.Id);
                if (!allowedLogin)
                    return AuthenticateResult.Fail($"DAISI: User not allowed to login.");

                claimsIdentity.AddClaim(new Claim(DaisiAuthentication.USER_ID_CLAIM, result.Owner.Id));
            }

            if (result.Owner?.AccountId is not null)
                claimsIdentity.AddClaim(new Claim(DaisiAuthentication.ACCOUNT_ID_CLAIM, result.Owner.AccountId));
            else
                Logger.LogInformation($"No Account ID found on Client Key \"{result.Key}\"");

            return AuthenticateResult.Success(new AuthenticationTicket(claimsPrincipal, Scheme.Name));


        }
    }
}
