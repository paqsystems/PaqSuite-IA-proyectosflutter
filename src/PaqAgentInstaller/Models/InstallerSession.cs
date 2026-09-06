using System.Text.Json.Serialization;

namespace PaqAgentInstaller.Models;

public sealed class InstallerSession
{
    public string AgentId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string AgentToken { get; set; } = "";
    public string GatewayUrl { get; set; } = InstallerDefaults.ProductionGatewayUrl;

    public string SqlServer { get; set; } = "";
    public int? SqlPort { get; set; }
    public string SqlDatabase { get; set; } = "";
    public string SqlUser { get; set; } = "";
    public string SqlPassword { get; set; } = "";
    public bool SqlEncrypt { get; set; } = true;
    public bool SqlTrustServerCertificate { get; set; } = true;

    public bool RuntimePresent { get; set; }
    public bool RuntimeAckContinue { get; set; }
    public bool SqlTestOk { get; set; }
    public bool GatewayTestOk { get; set; }
    public bool GatewayOverride { get; set; }

    public string InstallDirectory { get; set; } = "";
    public string LastMessage { get; set; } = "";
}

public static class InstallerDefaults
{
    public const string ProductionGatewayUrl = "https://gateway.paqsystems.com/agent-hub";
    public const string ServiceName = "PaqAgent";
    public const string DesktopRuntimeDownloadUrl =
        "https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.14-windows-x64-installer";
}

public sealed class AgentLocalSettingsDocument
{
    [JsonPropertyName("agentId")]
    public string AgentId { get; set; } = "";

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = "";

    [JsonPropertyName("agentToken")]
    public string AgentToken { get; set; } = "";

    [JsonPropertyName("gatewayUrl")]
    public string GatewayUrl { get; set; } = "";

    [JsonPropertyName("sql")]
    public AgentLocalSqlSettings Sql { get; set; } = new();
}

public sealed class AgentLocalSqlSettings
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = "";

    [JsonPropertyName("port")]
    public int? Port { get; set; }

    [JsonPropertyName("database")]
    public string Database { get; set; } = "";

    [JsonPropertyName("user")]
    public string User { get; set; } = "";

    [JsonPropertyName("password")]
    public string Password { get; set; } = "";

    [JsonPropertyName("encrypt")]
    public bool Encrypt { get; set; } = true;

    [JsonPropertyName("trustServerCertificate")]
    public bool TrustServerCertificate { get; set; } = true;
}
