using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PaqAgent.Tango;

/// <summary>
/// Lectura HKLM alineada a TR-019 / ProcesoTango legado (Astor Client luego Tango2000 llaves).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTangoVersionRegistryReader : ITangoVersionRegistryReader
{
    public TangoVersionInfo? Read(string llave)
    {
        if (string.IsNullOrWhiteSpace(llave) || llave.IndexOfAny([';', '"']) >= 0)
        {
            return null;
        }

        var wowPrefixes = Environment.Is64BitOperatingSystem
            ? new[] { @"WOW6432Node\", "" }
            : new[] { "" };

        foreach (var wow in wowPrefixes)
        {
            var astor = TryReadAstor(wow, llave);
            if (astor is not null)
            {
                return astor;
            }
        }

        foreach (var wow in wowPrefixes)
        {
            var tango2000 = TryReadTango2000(wow, llave);
            if (tango2000 is not null)
            {
                return tango2000;
            }
        }

        return null;
    }

    private static TangoVersionInfo? TryReadAstor(string wow, string llave)
    {
        var path = $@"SOFTWARE\{wow}Axoft\Astor\{llave}\Client";
        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key is null)
        {
            return null;
        }

        var version = key.GetValue("SystemVersion")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var systemDir = NormalizeSystemDir(key.GetValue("SystemDir")?.ToString());
        return new TangoVersionInfo { Version = version, SystemDir = systemDir };
    }

    private static TangoVersionInfo? TryReadTango2000(string wow, string llave)
    {
        var path = $@"SOFTWARE\{wow}axoft\Tango2000\llaves\{llave}";
        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key is null)
        {
            return null;
        }

        var version = key.GetValue("ClientVersion")?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var systemDir = NormalizeSystemDir(key.GetValue("SystemDir")?.ToString());
        return new TangoVersionInfo { Version = version, SystemDir = systemDir };
    }

    private static string? NormalizeSystemDir(string? systemDir)
    {
        if (string.IsNullOrWhiteSpace(systemDir))
        {
            return null;
        }

        var trimmed = systemDir.Trim();
        return trimmed.EndsWith('\\') || trimmed.EndsWith('/')
            ? trimmed
            : trimmed + "\\";
    }
}
