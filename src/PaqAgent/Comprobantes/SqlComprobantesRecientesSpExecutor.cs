using Microsoft.Data.SqlClient;

namespace PaqAgent.Comprobantes;

public sealed class SqlComprobantesRecientesSpExecutor : IComprobantesRecientesSpExecutor
{
    public const string StoredProcedureName = "dbo.PAQ_Comprobantes_Recientes";

    public async Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string codigoCliente,
        int dias,
        int limit,
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
        command.Parameters.Add(new SqlParameter("@dias", System.Data.SqlDbType.Int) { Value = dias });
        command.Parameters.Add(new SqlParameter("@limit", System.Data.SqlDbType.Int) { Value = limit });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
