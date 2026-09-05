using Microsoft.Data.SqlClient;
using PaqAgent.Options;

namespace PaqAgent;

internal static class SqlConnectionStringFactory
{
    public static string Build(SqlOptions sql, int connectTimeoutSeconds)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = sql.Server,
            InitialCatalog = sql.Database,
            UserID = sql.User,
            Password = sql.Password,
            Encrypt = sql.Encrypt,
            TrustServerCertificate = sql.TrustServerCertificate,
            ConnectTimeout = connectTimeoutSeconds
        };
        return builder.ConnectionString;
    }
}
