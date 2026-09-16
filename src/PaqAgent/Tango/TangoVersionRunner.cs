using System.Text.Json;
using PaqContracts;

namespace PaqAgent.Tango;

public sealed class TangoVersionOutcome
{
    public string Status { get; init; } = JobStatuses.Failed;
    public object? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class TangoVersionInfo
{
    public string? Version { get; init; }
    public string? SystemDir { get; init; }
}

public interface ITangoVersionRegistryReader
{
    TangoVersionInfo? Read(string llave);
}

public sealed class TangoVersionRunner
{
    private readonly ITangoVersionRegistryReader tangoVersionRegistryReader;

    public TangoVersionRunner(ITangoVersionRegistryReader tangoVersionRegistryReader)
    {
        this.tangoVersionRegistryReader = tangoVersionRegistryReader;
    }

    public Task<TangoVersionOutcome> RunAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var llave = ExtractString(parameters, "llave");
        if (string.IsNullOrWhiteSpace(llave))
        {
            return Task.FromResult(Fail(
                "INVALID_PARAMETERS",
                "El parametro llave es obligatorio (ej. 000205/012)."));
        }

        try
        {
            var info = tangoVersionRegistryReader.Read(llave.Trim());
            if (info is null || string.IsNullOrWhiteSpace(info.Version))
            {
                return Task.FromResult(Fail("NOT_FOUND", "No se encontro version de Tango en el registro para la llave."));
            }

            return Task.FromResult(new TangoVersionOutcome
            {
                Status = JobStatuses.Success,
                Data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["version"] = info.Version,
                    ["systemDir"] = info.SystemDir
                }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(Fail("REGISTRY_ERROR", ex.GetType().Name + ": " + ex.Message));
        }
    }

    private static string? ExtractString(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var raw) || raw is null)
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            _ => raw.ToString()
        };
    }

    private static TangoVersionOutcome Fail(string code, string message) =>
        new()
        {
            Status = JobStatuses.Failed,
            ErrorCode = code,
            ErrorMessage = message
        };
}
