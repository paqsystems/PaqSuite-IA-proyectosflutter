using Microsoft.Data.SqlClient;
using PaqAgent.Options;

namespace PaqAgent.Diagnostics;

public sealed class SqlConnectionPinger : ISqlConnectionPinger
{
    public async Task<SqlPingResult> PingAsync(SqlOptions sql, CancellationToken cancellationToken)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = sql.Server,
                InitialCatalog = sql.Database,
                UserID = sql.User,
                Password = sql.Password,
                Encrypt = true,
                TrustServerCertificate = true,
                ConnectTimeout = 5
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return new SqlPingResult { Ok = true };
        }
        catch (Exception ex)
        {
            return new SqlPingResult
            {
                Ok = false,
                ErrorMessage = ex.GetType().Name + ": " + ex.Message
            };
        }
    }
}
