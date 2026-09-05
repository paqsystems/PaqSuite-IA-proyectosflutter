using System.Collections.Concurrent;
using PaqContracts;

namespace PaqGateway.Services;

public sealed class AgentRegistration
{
    public required string AgentId { get; init; }
    public required string ClientId { get; init; }
    public required string ConnectionId { get; init; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string? LastSeenIp { get; set; }
    public string Readiness { get; set; } = "network_ok";
}

public interface IAgentRegistry
{
    void Register(AgentRegistration registration);
    void Touch(string agentId, DateTimeOffset at, string? lastSeenIp, string? readiness);
    void UnregisterByConnection(string connectionId);
    bool TryGet(string agentId, out AgentRegistration? registration);
    string ResolvePresence(AgentRegistration? registration, DateTimeOffset nowUtc, int ttlSeconds);
}

public sealed class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentRegistration> byAgentId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> agentIdByConnectionId = new(StringComparer.Ordinal);

    public void Register(AgentRegistration registration)
    {
        byAgentId[registration.AgentId] = registration;
        agentIdByConnectionId[registration.ConnectionId] = registration.AgentId;
    }

    public void Touch(string agentId, DateTimeOffset at, string? lastSeenIp, string? readiness)
    {
        if (!byAgentId.TryGetValue(agentId, out var existing))
        {
            return;
        }

        existing.LastSeenAt = at;
        if (lastSeenIp is not null)
        {
            existing.LastSeenIp = lastSeenIp;
        }

        if (!string.IsNullOrWhiteSpace(readiness))
        {
            existing.Readiness = readiness;
        }
    }

    public void UnregisterByConnection(string connectionId)
    {
        if (!agentIdByConnectionId.TryRemove(connectionId, out var agentId))
        {
            return;
        }

        byAgentId.TryRemove(agentId, out _);
    }

    public bool TryGet(string agentId, out AgentRegistration? registration)
    {
        if (byAgentId.TryGetValue(agentId, out var found))
        {
            registration = found;
            return true;
        }

        registration = null;
        return false;
    }

    public string ResolvePresence(AgentRegistration? registration, DateTimeOffset nowUtc, int ttlSeconds)
    {
        if (registration is null)
        {
            return AgentPresenceStatuses.Offline;
        }

        if (nowUtc - registration.LastSeenAt > TimeSpan.FromSeconds(ttlSeconds))
        {
            return AgentPresenceStatuses.Offline;
        }

        var readiness = registration.Readiness;
        if (string.Equals(readiness, "degraded", StringComparison.OrdinalIgnoreCase)
            || readiness.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return AgentPresenceStatuses.Degraded;
        }

        return AgentPresenceStatuses.Online;
    }
}
