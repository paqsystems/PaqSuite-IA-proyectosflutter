using Microsoft.Data.SqlClient;

namespace PaqAgent.Saldos;

public sealed class SqlSaldosConsultarSpExecutor : ISaldosConsultarSpExecutor
{
    public const string StoredProcedureName = "dbo.PAQ_Saldos_Consultar";

    public async Task<Dictionary<string, object?>?> ExecuteAsync(
        string connectionString,
        string codigoCliente,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new SqlCommand(StoredProcedureName, connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = Math.Max(1, timeoutSeconds)
        };
        command.Parameters.Add(new SqlParameter("@codigoCliente", System.Data.SqlDbType.NVarChar, 20)
        {
            Value = codigoCliente
        });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return row;
    }
}
