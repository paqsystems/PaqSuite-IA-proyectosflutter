namespace PaqAgentInstaller.Services;

public sealed class RuntimeDetectionResult
{
    public required bool IsPresent { get; init; }
    public string? HighestVersionFound { get; init; }
    public string Message { get; init; } = "";
}

public static class RuntimeDetector
{
    public static RuntimeDetectionResult DetectDotNet8DesktopX64()
    {
        var sharedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet",
            "shared",
            "Microsoft.WindowsDesktop.App");

        if (!Directory.Exists(sharedRoot))
        {
            return new RuntimeDetectionResult
            {
                IsPresent = false,
                Message = "No se encontró Microsoft.WindowsDesktop.App en Program Files\\dotnet\\shared."
            };
        }

        var versions = Directory.GetDirectories(sharedRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name!.StartsWith("8.", StringComparison.Ordinal))
            .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (versions.Count == 0)
        {
            return new RuntimeDetectionResult
            {
                IsPresent = false,
                Message = "Hay Desktop Runtime instalado, pero no la serie 8.x (se requiere .NET 8)."
            };
        }

        return new RuntimeDetectionResult
        {
            IsPresent = true,
            HighestVersionFound = versions[0],
            Message = $"Detectado .NET Desktop Runtime {versions[0]}."
        };
    }
}
