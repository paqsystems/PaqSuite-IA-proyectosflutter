using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PaqGateway.Options;

namespace PaqGateway.Services;

public sealed class AgentAuthRequest
{
    public string AgentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string AgentToken { get; set; } = "";
}

public enum AgentAuthOutcome
{
    Authorized,
    Invalid,
    Inactive
}

public interface IAgentAuthenticator
{
    Task<AgentAuthOutcome> AuthenticateAsync(AgentAuthRequest request, CancellationToken cancellationToken);
}

/// <summary>Stub solo Development + UseDevAuthStub (M5).</summary>
public sealed class DevStubAgentAuthenticator : IAgentAuthenticator
{
    private readonly IHostEnvironment environment;
    private readonly GatewayOptions gatewayOptions;

    public DevStubAgentAuthenticator(IHostEnvironment environment, IOptions<GatewayOptions> gatewayOptions)
    {
        this.environment = environment;
        this.gatewayOptions = gatewayOptions.Value;
    }

    public bool IsEnabled =>
        environment.IsDevelopment()
        && gatewayOptions.UseDevAuthStub
        && !environment.IsProduction();

    public Task<AgentAuthOutcome> AuthenticateAsync(AgentAuthRequest request, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("Dev auth stub is disabled outside Development+UseDevAuthStub.");
        }

        if (string.IsNullOrWhiteSpace(request.AgentId)
            || string.IsNullOrWhiteSpace(request.ClientId)
            || string.IsNullOrWhiteSpace(request.AgentToken))
        {
            return Task.FromResult(AgentAuthOutcome.Invalid);
        }

        return Task.FromResult(AgentAuthOutcome.Authorized);
    }
}

public sealed class LaravelAgentAuthenticator : IAgentAuthenticator
{
    public const string HttpClientName = "LaravelApi";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly LaravelApiOptions laravelApiOptions;
    private readonly IMemoryCache memoryCache;
    private readonly GatewayOptions gatewayOptions;
    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public LaravelAgentAuthenticator(
        IHttpClientFactory httpClientFactory,
        IOptions<LaravelApiOptions> laravelApiOptions,
        IOptions<GatewayOptions> gatewayOptions,
        IMemoryCache memoryCache)
    {
        this.httpClientFactory = httpClientFactory;
        this.laravelApiOptions = laravelApiOptions.Value;
        this.gatewayOptions = gatewayOptions.Value;
        this.memoryCache = memoryCache;
    }

    public async Task<AgentAuthOutcome> AuthenticateAsync(AgentAuthRequest request, CancellationToken cancellationToken)
    {
        var cacheKey = $"agent-auth:{request.AgentId}:{request.ClientId}:{request.AgentToken}";
        if (memoryCache.TryGetValue(cacheKey, out AgentAuthOutcome cached))
        {
            return cached;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/internal/gateway/authenticate");
        message.Headers.TryAddWithoutValidation("X-Paq-Internal-Api-Key", laravelApiOptions.InternalApiKey);
        message.Content = JsonContent.Create(request, options: jsonOptions);

        using var response = await client.SendAsync(message, cancellationToken);
        var outcome = response.StatusCode switch
        {
            System.Net.HttpStatusCode.OK => AgentAuthOutcome.Authorized,
            System.Net.HttpStatusCode.Forbidden => AgentAuthOutcome.Inactive,
            _ => AgentAuthOutcome.Invalid
        };

        memoryCache.Set(
            cacheKey,
            outcome,
            TimeSpan.FromSeconds(Math.Max(1, gatewayOptions.AgentTokenCacheSeconds)));

        return outcome;
    }
}

/// <summary>Elige stub Dev o Laravel según entorno/config.</summary>
public sealed class AgentAuthenticatorFacade : IAgentAuthenticator
{
    private readonly DevStubAgentAuthenticator devStub;
    private readonly LaravelAgentAuthenticator laravel;
    private readonly IHostEnvironment environment;
    private readonly GatewayOptions gatewayOptions;

    public AgentAuthenticatorFacade(
        DevStubAgentAuthenticator devStub,
        LaravelAgentAuthenticator laravel,
        IHostEnvironment environment,
        IOptions<GatewayOptions> gatewayOptions)
    {
        this.devStub = devStub;
        this.laravel = laravel;
        this.environment = environment;
        this.gatewayOptions = gatewayOptions.Value;
    }

    public Task<AgentAuthOutcome> AuthenticateAsync(AgentAuthRequest request, CancellationToken cancellationToken)
    {
        if (environment.IsProduction() && gatewayOptions.UseDevAuthStub)
        {
            throw new InvalidOperationException("UseDevAuthStub must not be enabled in Production.");
        }

        if (devStub.IsEnabled)
        {
            return devStub.AuthenticateAsync(request, cancellationToken);
        }

        return laravel.AuthenticateAsync(request, cancellationToken);
    }
}
