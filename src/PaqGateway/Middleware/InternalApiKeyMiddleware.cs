using Microsoft.Extensions.Options;
using PaqGateway.Options;

namespace PaqGateway.Middleware;

public sealed class InternalApiKeyMiddleware
{
    public const string HeaderName = "X-Paq-Internal-Api-Key";

    private readonly RequestDelegate next;

    public InternalApiKeyMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<GatewayOptions> gatewayOptions)
    {
        if (!context.Request.Path.StartsWithSegments("/internal"))
        {
            await next(context);
            return;
        }

        var expected = gatewayOptions.Value.InternalApiKey;
        if (string.IsNullOrEmpty(expected))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Internal API key is not configured.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var provided)
            || !string.Equals(provided.ToString(), expected, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
