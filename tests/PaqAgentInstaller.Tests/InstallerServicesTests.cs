using System.IO.Compression;
using PaqAgentInstaller.Models;
using PaqAgentInstaller.Services;

namespace PaqAgentInstaller.Tests;

public class CredentialValidatorTests
{
    [Fact]
    public void ValidateRequired_falla_sin_token()
    {
        var session = ValidSession();
        session.AgentToken = "";
        var errors = CredentialValidator.ValidateRequired(session);
        Assert.Contains(errors, e => e.Contains("AgentToken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateRequired_ok_completo()
    {
        var errors = CredentialValidator.ValidateRequired(ValidSession());
        Assert.Empty(errors);
    }

    [Fact]
    public void CanProceedPastRuntime_requiere_ack_si_falta()
    {
        var session = new InstallerSession { RuntimePresent = false, RuntimeAckContinue = false };
        Assert.False(CredentialValidator.CanProceedPastRuntime(session));
        session.RuntimeAckContinue = true;
        Assert.True(CredentialValidator.CanProceedPastRuntime(session));
    }

    private static InstallerSession ValidSession() => new()
    {
        AgentId = "a",
        ClientId = "c",
        AgentToken = "t",
        GatewayUrl = "https://gateway.paqsystems.com/agent-hub",
        SqlServer = "localhost",
        SqlDatabase = "dic",
        SqlUser = "u",
        SqlPassword = "p"
    };
}

public class SqlConnectionTesterTests
{
    [Fact]
    public void ResolveDataSource_puerto()
    {
        Assert.Equal("host,1433", SqlConnectionTester.ResolveDataSource("host", 1433));
        Assert.Equal(@"X\Y", SqlConnectionTester.ResolveDataSource(@"X\Y", 1433));
    }
}

public class AppSettingsLocalWriterTests
{
    [Fact]
    public void Write_genera_json_camelCase()
    {
        var dir = Path.Combine(Path.GetTempPath(), "paq-installer-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new InstallerSession
            {
                AgentId = "id1",
                ClientId = "cli",
                AgentToken = "tok",
                GatewayUrl = InstallerDefaults.ProductionGatewayUrl,
                SqlServer = "srv",
                SqlPort = 1433,
                SqlDatabase = "db",
                SqlUser = "u",
                SqlPassword = "p",
                SqlEncrypt = true,
                SqlTrustServerCertificate = true
            };
            AppSettingsLocalWriter.Write(session, dir);
            var json = File.ReadAllText(Path.Combine(dir, "appsettings.local.json"));
            Assert.Contains("\"agentId\"", json, StringComparison.Ordinal);
            Assert.Contains("\"agentToken\"", json, StringComparison.Ordinal);
            Assert.Contains("\"port\": 1433", json, StringComparison.Ordinal);
            Assert.DoesNotContain("dev-agent-token", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

public class RuntimeDetectorTests
{
    [Fact]
    public void Detect_no_lanza()
    {
        var result = RuntimeDetector.DetectDotNet8DesktopX64();
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}

public class AgentFilesCopierTests
{
    [Fact]
    public void InstallAgentBinaries_copia_carpeta_adjacente()
    {
        var root = Path.Combine(Path.GetTempPath(), "paq-agent-copy-" + Guid.NewGuid().ToString("N"));
        var agent = Path.Combine(root, "agent");
        var target = Path.Combine(root, "dest");
        try
        {
            Directory.CreateDirectory(agent);
            File.WriteAllText(Path.Combine(agent, "PaqAgent.exe"), "fake");
            AgentFilesCopier.InstallAgentBinaries(target, root);
            Assert.True(File.Exists(Path.Combine(target, "PaqAgent.exe")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAgentZip_extrae_paqagent()
    {
        var root = Path.Combine(Path.GetTempPath(), "paq-agent-zip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, "PaqAgent.exe"), "fake");
            var zipPath = Path.Combine(root, "p.zip");
            ZipFile.CreateFromDirectory(payload, zipPath);
            var dest = Path.Combine(root, "dest");
            using (var stream = File.OpenRead(zipPath))
            {
                AgentFilesCopier.ExtractAgentZip(stream, dest);
            }

            Assert.True(File.Exists(Path.Combine(dest, "PaqAgent.exe")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAgentZip_rechaza_path_traversal()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(@"..\evil.exe");
            using var writer = entry.Open();
            writer.WriteByte(1);
        }

        memory.Position = 0;
        var dest = Path.Combine(Path.GetTempPath(), "paq-zip-slip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            Assert.Throws<InvalidOperationException>(() => AgentFilesCopier.ExtractAgentZip(memory, dest));
        }
        finally
        {
            if (Directory.Exists(dest))
            {
                Directory.Delete(dest, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallAgentBinaries_falla_sin_agent_ni_payload()
    {
        var root = Path.Combine(Path.GetTempPath(), "paq-agent-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var dest = Path.Combine(root, "dest");
        try
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(
                () => AgentFilesCopier.InstallAgentBinaries(dest, root));
            Assert.Contains("PaqAgentSetup", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
