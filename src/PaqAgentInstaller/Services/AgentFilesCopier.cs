using System.IO.Compression;
using System.Reflection;

namespace PaqAgentInstaller.Services;

public static class AgentFilesCopier
{
    public const string EmbeddedPayloadResourceName = "PaqAgentInstaller.agent-payload.zip";

    public static string ResolveBundledAgentDirectory(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(root, "agent"));
    }

    public static bool DirectoryContainsAgent(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        var preferred = Path.Combine(directory, "PaqAgent.exe");
        if (File.Exists(preferred))
        {
            return true;
        }

        return Directory.GetFiles(directory, "PaqAgent.exe", SearchOption.AllDirectories).Length > 0;
    }

    public static void InstallAgentBinaries(string targetDirectory, string? baseDirectory = null)
    {
        var adjacent = ResolveBundledAgentDirectory(baseDirectory);
        if (DirectoryContainsAgent(adjacent))
        {
            CopyAgentFiles(adjacent, targetDirectory);
            return;
        }

        if (TryExtractEmbeddedPayload(targetDirectory))
        {
            return;
        }

        throw new DirectoryNotFoundException(
            "No se encontró la carpeta 'agent' junto al instalador ni el paquete embebido. "
            + "El PaqAgentSetup.exe está incompleto.");
    }

    public static void CopyAgentFiles(string sourceAgentDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceAgentDirectory))
        {
            throw new DirectoryNotFoundException(
                "No se encontró la carpeta 'agent' junto al instalador. El paquete de release está incompleto.");
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var file in Directory.GetFiles(sourceAgentDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceAgentDirectory, file);
            var dest = Path.Combine(targetDirectory, relative);
            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(file, dest, overwrite: true);
        }
    }

    public static bool TryExtractEmbeddedPayload(string targetDirectory, Assembly? assembly = null)
    {
        assembly ??= typeof(AgentFilesCopier).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedPayloadResourceName);
        if (stream is null)
        {
            return false;
        }

        ExtractAgentZip(stream, targetDirectory);
        return true;
    }

    public static void ExtractAgentZip(Stream zipStream, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var targetRoot = Path.GetFullPath(targetDirectory);
        if (!targetRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            targetRoot += Path.DirectorySeparatorChar;
        }

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName) || entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            var dest = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            if (!dest.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("El paquete embebido contiene una ruta no válida.");
            }

            var destDir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    public static string FindAgentExecutable(string targetDirectory)
    {
        var preferred = Path.Combine(targetDirectory, "PaqAgent.exe");
        if (File.Exists(preferred))
        {
            return preferred;
        }

        var any = Directory.GetFiles(targetDirectory, "PaqAgent.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (any is null)
        {
            throw new FileNotFoundException("No se encontró PaqAgent.exe en el directorio de instalación.");
        }

        return any;
    }
}
