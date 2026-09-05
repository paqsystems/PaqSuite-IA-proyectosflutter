using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PaqContracts;
using PaqGateway.Middleware;

namespace PaqGateway.Tests;

public sealed class GatewayWebApplicationFactory : WebApplicationFactory<Program>
{
    public FakeTimeProvider TimeProvider { get; } = new(DateTimeOffset.UtcNow);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:InternalApiKey"] = "test-internal-key",
                ["Gateway:UseDevAuthStub"] = "true",
                ["Gateway:AgentTokenCacheSeconds"] = "60",
                ["Gateway:OnlineTtlSeconds"] = "90"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(TimeProvider)).ToList();
            foreach (var descriptor in existing)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<TimeProvider>(TimeProvider);
        });
    }

    public HttpClient CreateAuthorizedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(InternalApiKeyMiddleware.HeaderName, "test-internal-key");
        return client;
    }
}

public class InternalApiKeyTests : IClassFixture<GatewayWebApplicationFactory>
{
    private readonly GatewayWebApplicationFactory factory;

    public InternalApiKeyTests(GatewayWebApplicationFactory factory) => this.factory = factory;

    [Fact]
    public async Task Internal_status_without_api_key_returns_401()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/internal/agents/any/status");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public class AgentPresenceTests : IClassFixture<GatewayWebApplicationFactory>
{
    private readonly GatewayWebApplicationFactory factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AgentPresenceTests(GatewayWebApplicationFactory factory) => this.factory = factory;

    [Fact]
    public async Task Agent_connects_online_then_expires_by_ttl()
    {
        await using var connection = await ConnectAgentAsync("ttl-agent-01");
        var client = factory.CreateAuthorizedClient();

        var online = await client.GetFromJsonAsync<AgentStatusResponse>(
            "/internal/agents/ttl-agent-01/status",
            JsonOptions);
        Assert.NotNull(online);
        Assert.Equal(AgentPresenceStatuses.Online, online.Status);

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(91));

        var offline = await client.GetFromJsonAsync<AgentStatusResponse>(
            "/internal/agents/ttl-agent-01/status",
            JsonOptions);
        Assert.NotNull(offline);
        Assert.Equal(AgentPresenceStatuses.Offline, offline.Status);

        await connection.DisposeAsync();
    }

    private async Task<HubConnection> ConnectAgentAsync(string agentId)
    {
        var url =
            $"http://localhost/agent-hub?agentId={Uri.EscapeDataString(agentId)}&clientId=lab&agentToken=lab-token-1";
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }
}

public class JobDispatchTests : IClassFixture<GatewayWebApplicationFactory>
{
    private readonly GatewayWebApplicationFactory factory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public JobDispatchTests(GatewayWebApplicationFactory factory) => this.factory = factory;

    [Fact]
    public async Task Jobs_send_without_agent_returns_offline()
    {
        var client = factory.CreateAuthorizedClient();
        var response = await client.PostAsJsonAsync(
            "/internal/jobs/send",
            new JobRequest
            {
                TraceId = "trace-offline",
                AgentId = "missing-agent",
                Operation = "diagnostics.run",
                TimeoutSeconds = 5
            },
            JsonOptions);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(JobStatuses.Offline, result.Status);
        Assert.Equal(ErrorCodes.AgentOffline, result.ErrorCode);
        Assert.Equal("trace-offline", result.TraceId);
        Assert.False(string.IsNullOrWhiteSpace(result.JobId));
    }

    [Fact]
    public async Task Jobs_send_to_mock_agent_returns_success()
    {
        await using var connection = await ConnectMockAgentAsync("job-agent-01");
        var client = factory.CreateAuthorizedClient();

        var response = await client.PostAsJsonAsync(
            "/internal/jobs/send",
            new JobRequest
            {
                TraceId = "trace-ok",
                AgentId = "job-agent-01",
                ClientId = "lab",
                Operation = "diagnostics.run",
                TimeoutSeconds = 10
            },
            JsonOptions);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JobResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(JobStatuses.Success, result.Status);
        Assert.Equal("trace-ok", result.TraceId);
        Assert.False(string.IsNullOrWhiteSpace(result.JobId));

        await connection.DisposeAsync();
    }

    private async Task<HubConnection> ConnectMockAgentAsync(string agentId)
    {
        var url =
            $"http://localhost/agent-hub?agentId={Uri.EscapeDataString(agentId)}&clientId=lab&agentToken=lab-token-1";
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();

        connection.On<JobRequest>(HubMethodNames.ExecuteJob, async request =>
        {
            await connection.InvokeAsync(
                HubMethodNames.CompleteJob,
                new JobResult
                {
                    TraceId = request.TraceId,
                    JobId = request.JobId,
                    Status = JobStatuses.Success,
                    Data = new { ok = true }
                });
        });

        await connection.StartAsync();
        return connection;
    }
}

public class DevStubGuardTests
{
    [Fact]
    public void Production_rejects_dev_auth_stub_flag()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Gateway:InternalApiKey"] = "prod-key",
                    ["Gateway:UseDevAuthStub"] = "true"
                });
            });
        });

        var ex = Assert.ThrowsAny<Exception>(() =>
        {
            _ = factory.CreateClient();
        });
        Assert.Contains("UseDevAuthStub", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
