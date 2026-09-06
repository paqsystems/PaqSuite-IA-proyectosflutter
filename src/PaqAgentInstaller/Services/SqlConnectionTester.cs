using Microsoft.Data.SqlClient;
using PaqAgentInstaller.Models;

namespace PaqAgentInstaller.Services;

public sealed class ConnectionTestResult
{
    public required bool Ok { get; init; }
    public string Message { get; init; } = "";
}

public static class SqlConnectionTester
{
    public static async Task<ConnectionTestResult> TestAsync(InstallerSession session, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = BuildConnectionString(session);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 5;
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            return new ConnectionTestResult
            {
                Ok = true,
                Message = "Conexión SQL correcta."
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult
            {
                Ok = false,
                Message = "No se pudo conectar al SQL: " + ex.Message
            };
        }
    }

    public static string BuildConnectionString(InstallerSession session)
    {
        var dataSource = ResolveDataSource(session.SqlServer, session.SqlPort);
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = dataSource,
            InitialCatalog = session.SqlDatabase.Trim(),
            UserID = session.SqlUser.Trim(),
            Password = session.SqlPassword,
            Encrypt = session.SqlEncrypt,
            TrustServerCertificate = session.SqlTrustServerCertificate,
            ConnectTimeout = 5
        };
        return builder.ConnectionString;
    }

    public static string ResolveDataSource(string server, int? port)
    {
        var trimmed = (server ?? "").Trim();
        if (port is > 0 && !trimmed.Contains('\\', StringComparison.Ordinal))
        {
            return $"{trimmed},{port.Value}";
        }

        return trimmed;
    }
}
