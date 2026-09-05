using System.Text.Json;
using Microsoft.Extensions.Options;
using PaqContracts;
using PaqGateway.Hubs;
using PaqGateway.Middleware;
using PaqGateway.Options;
using PaqGateway.Services;
using PaqGateway.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.Configure<LaravelApiOptions>(builder.Configuration.GetSection(LaravelApiOptions.SectionName));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAgentRegistry, AgentRegistry>();
builder.Services.AddSingleton<IJobCoordinator, JobCoordinator>();
builder.Services.AddSingleton<DevStubAgentAuthenticator>();
builder.Services.AddSingleton<LaravelAgentAuthenticator>();
builder.Services.AddSingleton<IAgentAuthenticator, AgentAuthenticatorFacade>();
builder.Services.AddSingleton<IJobDispatchService, JobDispatchService>();
builder.Services.AddHostedService<JobShutdownService>();

builder.Services.AddHttpClient(LaravelAgentAuthenticator.HttpClientName, (sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<LaravelApiOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    }
});

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

if (app.Environment.IsProduction())
{
    var gatewayOptions = app.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
    if (gatewayOptions.UseDevAuthStub)
    {
        throw new InvalidOperationException("Gateway:UseDevAuthStub cannot be true in Production.");
    }
}

app.UseMiddleware<InternalApiKeyMiddleware>();

app.MapHub<AgentHub>("/agent-hub");

app.MapGet("/internal/agents/{agentId}/status", (
    string agentId,
    IAgentRegistry registry,
    IOptions<GatewayOptions> gatewayOptions,
    TimeProvider timeProvider) =>
{
    var ttl = gatewayOptions.Value.OnlineTtlSeconds ?? AgentDefaults.OnlineTtlSeconds;
    registry.TryGet(agentId, out var registration);
    var status = registry.ResolvePresence(registration, timeProvider.GetUtcNow(), ttl);
    var response = new AgentStatusResponse
    {
        AgentId = agentId,
        Status = status,
        LastSeenAt = registration?.LastSeenAt,
        LastSeenIp = registration?.LastSeenIp
    };
    return Results.Json(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.MapPost("/internal/jobs/send", async (
    JobRequest request,
    IJobDispatchService dispatchService,
    CancellationToken cancellationToken) =>
{
    var result = await dispatchService.SendAsync(request, cancellationToken);
    return Results.Json(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
});

app.Run();

public partial class Program;
