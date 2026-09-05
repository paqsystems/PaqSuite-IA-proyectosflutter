namespace PaqGateway.Options;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public string InternalApiKey { get; set; } = "";

    /// <summary>Cache de validación de token (M3). Default 60.</summary>
    public int AgentTokenCacheSeconds { get; set; } = 60;

    /// <summary>Solo junto con Development (M5). Nunca en Production.</summary>
    public bool UseDevAuthStub { get; set; }

    /// <summary>Override de TTL para tests; null = AgentDefaults.OnlineTtlSeconds.</summary>
    public int? OnlineTtlSeconds { get; set; }
}

public sealed class LaravelApiOptions
{
    public const string SectionName = "LaravelApi";

    public string BaseUrl { get; set; } = "";
    public string InternalApiKey { get; set; } = "";
}
