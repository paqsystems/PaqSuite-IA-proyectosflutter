using Microsoft.Data.SqlClient;
using PaqAgent.Options;

namespace PaqAgent;

internal static class SqlConnectionStringFactory
{
    public static string Build(SqlOptions sql, int connectTimeoutSeconds, string? databaseOverride = null)
    {
        var catalog = string.IsNullOrWhiteSpace(databaseOverride)
            ? sql.Database
            : databaseOverride.Trim();

        if (string.IsNullOrWhiteSpace(catalog))
        {
            throw new InvalidOperationException("InitialCatalog / _database no puede ser vacío.");
        }

        if (catalog.IndexOfAny([';', '=', '"']) >= 0)
        {
            throw new InvalidOperationException("Nombre de base (_database) inválido.");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = ResolveDataSource(sql),
            InitialCatalog = catalog,
            UserID = sql.User,
            Password = sql.Password,
            Encrypt = sql.Encrypt,
            TrustServerCertificate = sql.TrustServerCertificate,
            ConnectTimeout = connectTimeoutSeconds
        };
        return builder.ConnectionString;
    }

    internal static string ResolveDataSource(SqlOptions sql)
    {
        var server = (sql.Server ?? "").Trim();
        if (sql.Port is > 0 && !server.Contains('\\', StringComparison.Ordinal))
        {
            return $"{server},{sql.Port.Value}";
        }

        return server;
    }
}
