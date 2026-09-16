using Microsoft.Data.SqlClient;

namespace PaqAgent.Articulos;

public sealed class SqlArticulosObtenerSpExecutor : IArticulosObtenerSpExecutor
{
    public const string StoredProcedureName = "dbo.PAQ_Articulos_Obtener";

    public async Task<Dictionary<string, object?>?> ExecuteAsync(
        string connectionString,
        string codigo,
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
        command.Parameters.Add(new SqlParameter("@codigo", System.Data.SqlDbType.NVarChar, 50) { Value = codigo });

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
