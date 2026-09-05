using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using PaqAgent.Auth;
using PaqAgent.Diagnostics;
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
    private readonly DiagnosticsRunner diagnosticsRunner;
    private readonly AuthLoginRunner authLoginRunner;
    private HubConnection? hubConnection;
    private string readiness = "network_ok";
    private readonly string agentVersion;
    private readonly string machineName;

    public AgentGatewayConnector(
        IOptions<AgentOptions> agentOptions,
        ILogger<AgentGatewayConnector> logger,
        TimeProvider timeProvider,
        DiagnosticsRunner diagnosticsRunner,
        AuthLoginRunner authLoginRunner)
    {
        this.agentOptions = agentOptions.Value;
        this.logger = logger;
        this.timeProvider = timeProvider;
        this.diagnosticsRunner = diagnosticsRunner;
        this.authLoginRunner = authLoginRunner;
        agentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        machineName = Environment.MachineName;
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

        readiness = "gateway_authenticated";

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
                    if (readiness is "network_ok")
                    {
                        readiness = "gateway_authenticated";
                    }

                    logger.LogInformation(
                        "Conectado al Gateway. readiness={Readiness} agentId={AgentId}",
                        readiness,
                        agentOptions.AgentId);
                }

                await SendHeartbeatAsync(stoppingToken).ConfigureAwait(false);
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

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
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

        if (string.Equals(request.Operation, JobOperations.DiagnosticsRun, StringComparison.Ordinal))
        {
            var outcome = await diagnosticsRunner
                .RunAsync(agentOptions, agentVersion, machineName, CancellationToken.None)
                .ConfigureAwait(false);
            readiness = outcome.Readiness;
            result = new JobResult
            {
                TraceId = request.TraceId,
                JobId = request.JobId,
                Status = outcome.Status,
                Data = outcome.Data,
                ErrorCode = outcome.ErrorCode,
                ErrorMessage = outcome.ErrorMessage,
                DurationMs = (long)(timeProvider.GetUtcNow() - started).TotalMilliseconds
            };
        }
        else if (string.Equals(request.Operation, JobOperations.AuthLogin, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Ejecutando auth.login jobId={JobId} traceId={TraceId} (password_hash omitido de logs)",
                request.JobId,
                request.TraceId);
            var outcome = await authLoginRunner
                .RunAsync(agentOptions, request.Parameters, request.TimeoutSeconds, CancellationToken.None)
                .ConfigureAwait(false);
            result = new JobResult
            {
                TraceId = request.TraceId,
                JobId = request.JobId,
                Status = outcome.Status,
                Data = outcome.Data,
                ErrorCode = outcome.ErrorCode,
                ErrorMessage = outcome.ErrorMessage,
                DurationMs = (long)(timeProvider.GetUtcNow() - started).TotalMilliseconds
            };
        }
        else if (readiness is "network_ok")
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
        else
        {
            result = new JobResult
            {
                TraceId = request.TraceId,
                JobId = request.JobId,
                Status = JobStatuses.Failed,
                ErrorCode = "OPERATION_NOT_ALLOWED",
                ErrorMessage =
                    $"operation '{request.Operation}' not in whitelist (MVP: {JobOperations.DiagnosticsRun}, {JobOperations.AuthLogin})",
                DurationMs = (long)(timeProvider.GetUtcNow() - started).TotalMilliseconds
            };
        }

        await hubConnection.InvokeAsync(HubMethodNames.CompleteJob, result).ConfigureAwait(false);
        logger.LogInformation(
            "CompleteJob enviado status={Status} jobId={JobId} traceId={TraceId} durationMs={DurationMs} readiness={Readiness}",
            result.Status,
            result.JobId,
            result.TraceId,
            result.DurationMs,
            readiness);
    }
}
