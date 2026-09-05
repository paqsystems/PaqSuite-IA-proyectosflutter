namespace PaqAgent;

/// <summary>Arma la URL del hub con query M8 (sin loguear el token).</summary>
public static class HubUrlBuilder
{
    public static string BuildHubUrl(string gatewayUrl, string agentId, string clientId, string agentToken)
    {
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            throw new ArgumentException("GatewayUrl is required.", nameof(gatewayUrl));
        }

        var trimmed = gatewayUrl.Trim().TrimEnd('/');
        if (!trimmed.EndsWith("/agent-hub", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/agent-hub";
        }

        var separator = trimmed.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{trimmed}{separator}agentId={Uri.EscapeDataString(agentId)}"
            + $"&clientId={Uri.EscapeDataString(clientId)}"
            + $"&agentToken={Uri.EscapeDataString(agentToken)}";
    }

    /// <summary>URL segura para logs (sin agentToken).</summary>
    public static string BuildSafeHubUrlForLogs(string gatewayUrl, string agentId, string clientId)
    {
        var trimmed = gatewayUrl.Trim().TrimEnd('/');
        if (!trimmed.EndsWith("/agent-hub", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/agent-hub";
        }

        return $"{trimmed}?agentId={Uri.EscapeDataString(agentId)}&clientId={Uri.EscapeDataString(clientId)}&agentToken=***";
    }
}
