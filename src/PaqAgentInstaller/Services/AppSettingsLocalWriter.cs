using System.Text.Json;
using PaqAgentInstaller.Models;

namespace PaqAgentInstaller.Services;

public static class AppSettingsLocalWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Write(InstallerSession session, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, "appsettings.local.json");
        var document = new AgentLocalSettingsDocument
        {
            AgentId = session.AgentId.Trim(),
            ClientId = session.ClientId.Trim(),
            AgentToken = session.AgentToken,
            GatewayUrl = session.GatewayUrl.Trim(),
            Sql = new AgentLocalSqlSettings
            {
                Server = session.SqlServer.Trim(),
                Port = session.SqlPort is > 0 ? session.SqlPort : null,
                Database = session.SqlDatabase.Trim(),
                User = session.SqlUser.Trim(),
                Password = session.SqlPassword,
                Encrypt = session.SqlEncrypt,
                TrustServerCertificate = session.SqlTrustServerCertificate
            }
        };

        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static bool Exists(string targetDirectory) =>
        File.Exists(Path.Combine(targetDirectory, "appsettings.local.json"));
}
