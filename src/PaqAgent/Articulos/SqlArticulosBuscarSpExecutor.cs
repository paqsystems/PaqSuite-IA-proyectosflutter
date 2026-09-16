using Microsoft.Data.SqlClient;

namespace PaqAgent.Articulos;

public sealed class SqlArticulosBuscarSpExecutor : IArticulosBuscarSpExecutor
{
    public const string StoredProcedureName = "dbo.PAQ_Articulos_Buscar";

    public async Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
        string connectionString,
        string texto,
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
        command.Parameters.Add(new SqlParameter("@texto", System.Data.SqlDbType.NVarChar, 100) { Value = texto });
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
