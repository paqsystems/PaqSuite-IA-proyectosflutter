using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent;

public sealed class AgentGatewayConnector : BackgroundService
{
    private static readonly TimeSpan[] reconnectDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    private readonly AgentOptions agentOptions;
    private readonly ILogger<AgentGatewayConnector> logger;
    private readonly TimeProvider timeProvider;
    private HubConnection? hubConnection;
    private string readiness = "network_ok";

    public AgentGatewayConnector(
        IOptions<AgentOptions> agentOptions,
        ILogger<AgentGatewayConnector> logger,
        TimeProvider timeProvider)
    {
        this.agentOptions = agentOptions.Value;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!agentOptions.HasRequiredIdentity)
        {
            logger.LogError(
                "PaqAgent no inicia conexión: faltan agentId, clientId, agentToken o gatewayUrl. Completar appsettings.local.json.");
            return;
        }

        if (string.Equals(agentOptions.AgentToken, "dev-agent-token", StringComparison.Ordinal))
        {
            logger.LogError("PaqAgent rechaza AgentToken prohibido (dev-agent-token). Usar token real de alta.");
            return;
        }

        var agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        var machineName = Environment.MachineName;
        var sqlServerName = agentOptions.HasSqlConfig ? agentOptions.Sql.Server : null;

        logger.LogInformation(
            "PaqAgent starting machineName={MachineName} sqlServerName={SqlServerName} agentVersion={AgentVersion} agentId={AgentId} hub={Hub}",
            machineName,
            sqlServerName ?? "(none)",
            agentVersion,
            agentOptions.AgentId,
            HubUrlBuilder.BuildSafeHubUrlForLogs(
                agentOptions.GatewayUrl,
                agentOptions.AgentId,
                agentOptions.ClientId));

        readiness = await ResolveInitialReadinessAsync(stoppingToken).ConfigureAwait(false);

        var hubUrl = HubUrlBuilder.BuildHubUrl(
            agentOptions.GatewayUrl,
            agentOptions.AgentId,
            agentOptions.ClientId,
            agentOptions.AgentToken);

        hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(reconnectDelays)
            .Build();

        hubConnection.On<JobRequest>(HubMethodNames.ExecuteJob, async request =>
        {
            await HandleExecuteJobAsync(request).ConfigureAwait(false);
        });

        hubConnection.Reconnecting += error =>
        {
            readiness = "network_ok";
            logger.LogWarning(error, "Reconectando al Gateway");
            return Task.CompletedTask;
        };

        hubConnection.Reconnected += connectionId =>
        {
            readiness = "gateway_authenticated";
            logger.LogInformation("Reconectado al Gateway connectionId={ConnectionId}", connectionId);
            return Task.CompletedTask;
        };

        hubConnection.Closed += error =>
        {
            readiness = "network_ok";
            if (error is null)
            {
                logger.LogWarning("Conexión al Gateway cerrada");
            }
            else
            {
                logger.LogError(error, "Conexión al Gateway cerrada con error (token inválido u otro). El agente no queda online.");
            }

            return Task.CompletedTask;
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (hubConnection.State == HubConnectionState.Disconnected)
                {
                    logger.LogInformation("Conectando al Gateway…");
                    await hubConnection.StartAsync(stoppingToken).ConfigureAwait(false);
                    readiness = await ResolveInitialReadinessAsync(stoppingToken).ConfigureAwait(false);
                    if (readiness is "network_ok")
                    {
                        readiness = "gateway_authenticated";
                    }

                    logger.LogInformation(
                        "Conectado al Gateway. readiness={Readiness} agentId={AgentId}",
                        readiness,
                        agentOptions.AgentId);
                }

                await SendHeartbeatAsync(agentVersion, stoppingToken).ConfigureAwait(false);
                await Task.Delay(
                    TimeSpan.FromSeconds(AgentDefaults.HeartbeatSeconds),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                readiness = "network_ok";
                logger.LogError(
                    ex,
                    "Fallo de conexión/heartbeat al Gateway. Reintento con backoff. agentId={AgentId}",
                    agentOptions.AgentId);

                if (hubConnection.State != HubConnectionState.Disconnected)
                {
                    try
                    {
                        await hubConnection.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                try
                {
                    await Task.Delay(reconnectDelays[0], stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SendHeartbeatAsync(string agentVersion, CancellationToken cancellationToken)
    {
        if (hubConnection is null || hubConnection.State != HubConnectionState.Connected)
        {
            return;
        }

        await hubConnection.InvokeAsync(
            HubMethodNames.Heartbeat,
            new AgentHeartbeat
            {
                AgentId = agentOptions.AgentId,
                ClientId = agentOptions.ClientId,
                AgentVersion = agentVersion,
                Readiness = readiness,
                TimestampUtc = timeProvider.GetUtcNow()
            },
            cancellationToken).ConfigureAwait(false);

        logger.LogDebug("Heartbeat OK readiness={Readiness}", readiness);
    }

    private async Task HandleExecuteJobAsync(JobRequest request)
    {
        if (hubConnection is null)
        {
            return;
        }

        logger.LogInformation(
            "ExecuteJob recibido operation={Operation} jobId={JobId} traceId={TraceId}",
            request.Operation,
            request.JobId,
            request.TraceId);

        var started = timeProvider.GetUtcNow();
        JobResult result;

        if (!string.Equals(readiness, "operational", StringComparison.Ordinal)
            && !string.Equals(readiness, "gateway_authenticated", StringComparison.Ordinal)
            && !string.Equals(readiness, "sql_connection_ok", StringComparison.Ordinal)
            && !string.Equals(readiness, "schema_ready", StringComparison.Ordinal))
        {
            result = new JobResult
            {
                TraceId = request.TraceId,
                JobId = request.JobId,
                Status = JobStatuses.Degraded,
                ErrorCode = "AGENT_NOT_READY",
                ErrorMessage = $"readiness={readiness}",
                DurationMs = (long)(timeProvider.GetUtcNow() - started).TotalMilliseconds
            };
        }
        else if (agentOptions.HasSqlConfig
                 && !string.Equals(readiness, "sql_connection_ok", StringComparison.Ordinal)
                 && !string.Equals(readiness, "schema_ready", StringComparison.Ordinal)
                 && !string.Equals(readiness, "operational", StringComparison.Ordinal))
        {
            // SQL configurado pero no verificado aún
            result = new JobResult
            {
                TraceId = request.TraceId,
                JobId = request.JobId,
                Status = JobStatuses.Degraded,
                ErrorCode = "SQL_NOT_READY",
                ErrorMessage = "SQL configurado; verificación pendiente (TR-006 para diagnostics).",
                DurationMs = (long)(timeProvider.GetUtcNow() - started).TotalMilliseconds
            };
        }
        else
        {
            // TR-005: stub seguro sin SQL libre. diagnostics.run profundo = TR-006.
            result = new JobResult
            {
                TraceId = request.TraceId,
                JobId = request.JobId,
                Status = JobStatuses.Success,
                Data = new
                {
                    mock = true,
                    operation = request.Operation,
                    readiness,
                    note = "TR-005 stub; SQL lista blanca en TR-006"
                },
                DurationMs = (long)(timeProvider.GetUtcNow() - started).TotalMilliseconds
            };
        }

        await hubConnection.InvokeAsync(HubMethodNames.CompleteJob, result).ConfigureAwait(false);
        logger.LogInformation(
            "CompleteJob enviado status={Status} jobId={JobId} traceId={TraceId}",
            result.Status,
            result.JobId,
            result.TraceId);
    }

    private async Task<string> ResolveInitialReadinessAsync(CancellationToken cancellationToken)
    {
        if (!agentOptions.HasSqlConfig)
        {
            return "gateway_authenticated";
        }

        try
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = agentOptions.Sql.Server,
                InitialCatalog = agentOptions.Sql.Database,
                UserID = agentOptions.Sql.User,
                Password = agentOptions.Sql.Password,
                Encrypt = true,
                TrustServerCertificate = true,
                ConnectTimeout = 5
            };

            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("SQL lab alcanzable server={Server} database={Database}", agentOptions.Sql.Server, agentOptions.Sql.Database);
            return "sql_connection_ok";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQL lab no alcanzable; readiness=gateway_authenticated");
            return "gateway_authenticated";
        }
    }
}
