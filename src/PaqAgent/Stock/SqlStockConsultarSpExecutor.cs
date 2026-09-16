using Microsoft.Data.SqlClient;

namespace PaqAgent.Stock;

public sealed class SqlStockConsultarSpExecutor : IStockConsultarSpExecutor
{
    public const string StoredProcedureName = "dbo.PAQ_Stock_Consultar";

    public async Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string codigoArticulo,
        string? deposito,
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
        command.Parameters.Add(new SqlParameter("@codigoArticulo", System.Data.SqlDbType.NVarChar, 50)
        {
            Value = codigoArticulo
        });
        command.Parameters.Add(new SqlParameter("@deposito", System.Data.SqlDbType.NVarChar, 20)
        {
            Value = string.IsNullOrWhiteSpace(deposito) ? DBNull.Value : deposito
        });

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
