using Microsoft.Data.SqlClient;
using PaqAgent.Options;

namespace PaqAgent;

internal static class SqlConnectionStringFactory
{
    public static string Build(SqlOptions sql, int connectTimeoutSeconds)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = ResolveDataSource(sql),
            InitialCatalog = sql.Database,
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
