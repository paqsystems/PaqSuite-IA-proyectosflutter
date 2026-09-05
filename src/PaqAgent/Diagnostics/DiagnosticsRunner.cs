using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Diagnostics;

public sealed class DiagnosticsData
{
    public string AgentId { get; init; } = "";
    public string AgentVersion { get; init; } = "";
    public bool SqlConnectionOk { get; init; }
    public string Readiness { get; init; } = "network_ok";
    public string? MachineName { get; init; }
    public string? SqlServerName { get; init; }
    public string? SchemaVersionApplied { get; init; }
}

public sealed class DiagnosticsOutcome
{
    public string Status { get; init; } = JobStatuses.Degraded;
    public DiagnosticsData Data { get; init; } = new();
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string Readiness { get; init; } = "gateway_authenticated";
}

public sealed class DiagnosticsRunner
{
    private readonly ISqlConnectionPinger sqlConnectionPinger;

    public DiagnosticsRunner(ISqlConnectionPinger sqlConnectionPinger)
    {
        this.sqlConnectionPinger = sqlConnectionPinger;
    }

    public async Task<DiagnosticsOutcome> RunAsync(
        AgentOptions agentOptions,
        string agentVersion,
        string machineName,
        CancellationToken cancellationToken)
    {
        if (!agentOptions.HasSqlConfig)
        {
            var degradedNoSql = new DiagnosticsData
            {
                AgentId = agentOptions.AgentId,
                AgentVersion = agentVersion,
                SqlConnectionOk = false,
                Readiness = "gateway_authenticated",
                MachineName = machineName,
                SqlServerName = null
            };

            return new DiagnosticsOutcome
            {
                Status = JobStatuses.Degraded,
                Data = degradedNoSql,
                Readiness = degradedNoSql.Readiness,
                ErrorCode = "SQL_NOT_CONFIGURED",
                ErrorMessage = "sql.server/database no configurados en appsettings.local.json"
            };
        }

        var ping = await sqlConnectionPinger.PingAsync(agentOptions.Sql, cancellationToken).ConfigureAwait(false);
        if (!ping.Ok)
        {
            var degraded = new DiagnosticsData
            {
                AgentId = agentOptions.AgentId,
                AgentVersion = agentVersion,
                SqlConnectionOk = false,
                Readiness = "gateway_authenticated",
                MachineName = machineName,
                SqlServerName = agentOptions.Sql.Server
            };

            return new DiagnosticsOutcome
            {
                Status = JobStatuses.Degraded,
                Data = degraded,
                Readiness = degraded.Readiness,
                ErrorCode = "SQL_UNREACHABLE",
                ErrorMessage = ping.ErrorMessage
            };
        }

        // MVP TR-006: ping OK ⇒ operational (sin chequeo schema_ready separado aún).
        var success = new DiagnosticsData
        {
            AgentId = agentOptions.AgentId,
            AgentVersion = agentVersion,
            SqlConnectionOk = true,
            Readiness = "operational",
            MachineName = machineName,
            SqlServerName = agentOptions.Sql.Server
        };

        return new DiagnosticsOutcome
        {
            Status = JobStatuses.Success,
            Data = success,
            Readiness = success.Readiness
        };
    }
}
