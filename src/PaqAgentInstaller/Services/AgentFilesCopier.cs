namespace PaqAgentInstaller.Services;

public static class AgentFilesCopier
{
    public static string ResolveBundledAgentDirectory(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(root, "agent"));
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
