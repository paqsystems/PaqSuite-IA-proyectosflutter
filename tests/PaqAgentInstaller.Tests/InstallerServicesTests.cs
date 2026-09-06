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
