namespace Daisi.Integration.Tests.Infrastructure;

/// <summary>
/// Delegating handler that sets the response version to match the request version.
/// Required for gRPC over WebApplicationFactory, which needs HTTP/2 version matching.
/// </summary>
public class ResponseVersionHandler : DelegatingHandler
{
    public ResponseVersionHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        response.Version = request.Version;
        return response;
    }
}
