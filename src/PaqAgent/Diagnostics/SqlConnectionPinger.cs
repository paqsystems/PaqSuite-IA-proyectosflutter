using PaqAgent.Options;

namespace PaqAgent.Diagnostics;

public sealed class SqlConnectionPinger : ISqlConnectionPinger
{
    public async Task<SqlPingResult> PingAsync(SqlOptions sql, CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = SqlConnectionStringFactory.Build(sql, connectTimeoutSeconds: 5);
            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
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
