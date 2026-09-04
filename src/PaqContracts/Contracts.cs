namespace PaqContracts;

/// <summary>Estados de job del contrato MVP (D12).</summary>
public static class JobStatuses
{
    public const string Success = "success";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
    public const string Offline = "offline";
    public const string Degraded = "degraded";
    public const string Cancelled = "cancelled";
}

public sealed class JobRequest
{
    public string TraceId { get; set; } = "";
    public string AgentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Operation { get; set; } = "";
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class JobResult
{
    public string TraceId { get; set; } = "";
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public object? Data { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>Heartbeat con extension points de versión (plan SQL fase 2).</summary>
public sealed class AgentHeartbeat
{
    public string AgentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string? SchemaVersionApplied { get; set; }
    public string Readiness { get; set; } = "network_ok";
    public DateTimeOffset TimestampUtc { get; set; }
}

/// <summary>Heartbeat 30 s / TTL online 90 s (default scaffold H8). No es lógica de HU.</summary>
public static class AgentDefaults
{
    public const int HeartbeatSeconds = 30;
    public const int OnlineTtlSeconds = 90;
}
