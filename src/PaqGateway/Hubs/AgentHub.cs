using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PaqContracts;
using PaqGateway.Options;
using PaqGateway.Services;

namespace PaqGateway.Hubs;

public interface IAgentClient
{
    Task ExecuteJob(JobRequest request);
}

public sealed class AgentHub : Hub<IAgentClient>
{
    private readonly IAgentAuthenticator authenticator;
    private readonly IAgentRegistry registry;
    private readonly IJobCoordinator jobCoordinator;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AgentHub> logger;

    public AgentHub(
        IAgentAuthenticator authenticator,
        IAgentRegistry registry,
        IJobCoordinator jobCoordinator,
        TimeProvider timeProvider,
        ILogger<AgentHub> logger)
    {
        this.authenticator = authenticator;
        this.registry = registry;
        this.jobCoordinator = jobCoordinator;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext()
            ?? throw new HubException("Missing HTTP context");

        var agentId = http.Request.Query["agentId"].ToString();
        var clientId = http.Request.Query["clientId"].ToString();
        var agentToken = http.Request.Query["agentToken"].ToString();

        var outcome = await authenticator.AuthenticateAsync(
            new AgentAuthRequest
            {
                AgentId = agentId,
                ClientId = clientId,
                AgentToken = agentToken
            },
            Context.ConnectionAborted);

        if (outcome != AgentAuthOutcome.Authorized)
        {
            logger.LogWarning("Agent hub auth rejected for agentId={AgentId} outcome={Outcome}", agentId, outcome);
            Context.Abort();
            return;
        }

        var now = timeProvider.GetUtcNow();
        var remoteIp = http.Connection.RemoteIpAddress?.ToString();
        registry.Register(new AgentRegistration
        {
            AgentId = agentId,
            ClientId = clientId,
            ConnectionId = Context.ConnectionId,
            LastSeenAt = now,
            LastSeenIp = remoteIp,
            Readiness = "gateway_authenticated"
        });

        Context.Items["agentId"] = agentId;
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        registry.UnregisterByConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task Heartbeat(AgentHeartbeat heartbeat)
    {
        var agentId = Context.Items["agentId"] as string ?? heartbeat.AgentId;
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return Task.CompletedTask;
        }

        var http = Context.GetHttpContext();
        var remoteIp = http?.Connection.RemoteIpAddress?.ToString();
        registry.Touch(
            agentId,
            timeProvider.GetUtcNow(),
            remoteIp,
            string.IsNullOrWhiteSpace(heartbeat.Readiness) ? null : heartbeat.Readiness);

        return Task.CompletedTask;
    }

    public Task CompleteJob(JobResult result)
    {
        jobCoordinator.TryComplete(result);
        return Task.CompletedTask;
    }
}
