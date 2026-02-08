using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Daisi.Orc.Grpc.Authentication
{
    public class HostsOnlyAttribute : TypeFilterAttribute
    {
        public HostsOnlyAttribute() : base(typeof(HostFilter))
        {
        }
    }

    public class HostFilter : IAuthorizationFilter, IAsyncActionFilter
    {
        public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var hasClaim = context.HttpContext.User.Claims.Any(c => c.Type == "KeyType" && c.Value == "Host");
            if (!hasClaim)
                context.Result = new ForbidResult();

            return Task.CompletedTask;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var hasClaim = context.HttpContext.User.Claims.Any(c => c.Type == "KeyType" && c.Value == "Host");
            if (!hasClaim)
                context.Result = new ForbidResult();
        }
    }
}
