using PaqAgent.Options;

namespace PaqAgent.Diagnostics;

public sealed class SqlPingResult
{
    public bool Ok { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface ISqlConnectionPinger
{
    Task<SqlPingResult> PingAsync(SqlOptions sql, CancellationToken cancellationToken);
}
