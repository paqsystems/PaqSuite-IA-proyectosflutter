using Microsoft.Data.SqlClient;

namespace PaqAgent.Menu;

public sealed class SqlMenuAuthorizedSpExecutor : IMenuAuthorizedSpExecutor
{
    public const string StoredProcedureName = "dbo.PAQ_User_Menu_Authorized";

    public async Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
        string connectionString,
        int userId,
        int empresaId,
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
        command.Parameters.Add(new SqlParameter("@UserId", System.Data.SqlDbType.Int) { Value = userId });
        command.Parameters.Add(new SqlParameter("@EmpresaId", System.Data.SqlDbType.Int) { Value = empresaId });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var allResultSets = new List<IReadOnlyList<Dictionary<string, object?>>>();

        do
        {
            allResultSets.Add(await ReadResultSetAsync(reader, cancellationToken).ConfigureAwait(false));
        }
        while (await reader.NextResultAsync(cancellationToken).ConfigureAwait(false));

        return allResultSets;
    }

    private static async Task<IReadOnlyList<Dictionary<string, object?>>> ReadResultSetAsync(
        SqlDataReader reader,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                row[name] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }
}
