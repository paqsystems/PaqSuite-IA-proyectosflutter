using System.Diagnostics;
using System.ServiceProcess;
using PaqAgentInstaller.Models;

namespace PaqAgentInstaller.Services;

public sealed class ServiceInstallResult
{
    public required bool Ok { get; init; }
    public string Message { get; init; } = "";
}

public static class WindowsServiceInstaller
{
    public static ServiceInstallResult InstallAndStart(string binPath, string displayName = "PaqAgent")
    {
        var serviceName = InstallerDefaults.ServiceName;
        try
        {
            if (ServiceExists(serviceName))
            {
                RunSc($"stop {serviceName}");
                RunSc($"delete {serviceName}");
                Thread.Sleep(1500);
            }

            var quoted = $"\"{binPath}\"";
            RunSc($"create {serviceName} binPath= {quoted} start= auto DisplayName= \"{displayName}\"");
            RunSc($"description {serviceName} \"PaqSuite Agent Gateway client\"");
            RunSc($"start {serviceName}");

            using var controller = new ServiceController(serviceName);
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

            return new ServiceInstallResult
            {
                Ok = true,
                Message = $"Servicio {serviceName} en estado Running (start=auto)."
            };
        }
        catch (Exception ex)
        {
            return new ServiceInstallResult
            {
                Ok = false,
                Message = "No se pudo crear/iniciar el servicio (¿ejecutó como Administrador?): " + ex.Message
            };
        }
    }

    public static string? GetStatus(string serviceName = InstallerDefaults.ServiceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            return controller.Status.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool ServiceExists(string serviceName)
    {
        return ServiceController.GetServices().Any(s =>
            string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static void RunSc(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar sc.exe.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"sc {arguments} → {process.ExitCode}: {stdout} {stderr}".Trim());
        }
    }
}
