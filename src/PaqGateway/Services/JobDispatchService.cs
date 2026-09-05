using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PaqContracts;
using PaqGateway.Hubs;
using PaqGateway.Options;

namespace PaqGateway.Services;

public interface IJobDispatchService
{
    Task<JobResult> SendAsync(JobRequest request, CancellationToken cancellationToken);
}

public sealed class JobDispatchService : IJobDispatchService
{
    private readonly IAgentRegistry registry;
    private readonly IJobCoordinator jobCoordinator;
    private readonly IHubContext<AgentHub, IAgentClient> hubContext;
    private readonly GatewayOptions gatewayOptions;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<JobDispatchService> logger;

    public JobDispatchService(
        IAgentRegistry registry,
        IJobCoordinator jobCoordinator,
        IHubContext<AgentHub, IAgentClient> hubContext,
        IOptions<GatewayOptions> gatewayOptions,
        TimeProvider timeProvider,
        ILogger<JobDispatchService> logger)
    {
        this.registry = registry;
        this.jobCoordinator = jobCoordinator;
        this.hubContext = hubContext;
        this.gatewayOptions = gatewayOptions.Value;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public async Task<JobResult> SendAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var jobId = jobCoordinator.CreateJobId();
        request.JobId = jobId;

        if (string.IsNullOrWhiteSpace(request.TraceId) || string.IsNullOrWhiteSpace(request.AgentId))
        {
            return new JobResult
            {
                TraceId = request.TraceId,
                JobId = jobId,
                Status = JobStatuses.Failed,
                ErrorMessage = "traceId and agentId are required",
                DurationMs = ElapsedMs(started)
            };
        }

        var ttl = gatewayOptions.OnlineTtlSeconds ?? AgentDefaults.OnlineTtlSeconds;
        registry.TryGet(request.AgentId, out var registration);
        var presence = registry.ResolvePresence(registration, timeProvider.GetUtcNow(), ttl);
        if (presence == AgentPresenceStatuses.Offline || registration is null)
        {
            logger.LogInformation(
                "Job offline agentId={AgentId} traceId={TraceId} jobId={JobId}",
                request.AgentId,
                request.TraceId,
                jobId);
            return new JobResult
            {
                TraceId = request.TraceId,
                JobId = jobId,
                Status = JobStatuses.Offline,
                ErrorCode = ErrorCodes.AgentOffline,
                DurationMs = ElapsedMs(started)
            };
        }

        var pending = jobCoordinator.RegisterPending(jobId);

        try
        {
            await hubContext.Clients.Client(registration.ConnectionId).ExecuteJob(request);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dispatch failed traceId={TraceId} jobId={JobId}", request.TraceId, jobId);
            jobCoordinator.TryComplete(new JobResult
            {
                TraceId = request.TraceId,
                JobId = jobId,
                Status = JobStatuses.Failed,
                ErrorMessage = "Failed to dispatch to agent",
                DurationMs = ElapsedMs(started)
            });
        }

        var timeout = TimeSpan.FromSeconds(request.TimeoutSeconds <= 0 ? 30 : request.TimeoutSeconds);
        try
        {
            var completed = await pending.Task.WaitAsync(timeout, cancellationToken);
            completed.TraceId = string.IsNullOrWhiteSpace(completed.TraceId) ? request.TraceId : completed.TraceId;
            completed.JobId = string.IsNullOrWhiteSpace(completed.JobId) ? jobId : completed.JobId;
            if (completed.DurationMs == 0)
            {
                completed.DurationMs = ElapsedMs(started);
            }

            logger.LogInformation(
                "Job done status={Status} traceId={TraceId} jobId={JobId} durationMs={DurationMs}",
                completed.Status,
                completed.TraceId,
                completed.JobId,
                completed.DurationMs);
            return completed;
        }
        catch (TimeoutException)
        {
            jobCoordinator.TryComplete(new JobResult
            {
                TraceId = request.TraceId,
                JobId = jobId,
                Status = JobStatuses.Timeout,
                ErrorCode = ErrorCodes.AgentTimeout,
                DurationMs = ElapsedMs(started)
            });
            return new JobResult
            {
                TraceId = request.TraceId,
                JobId = jobId,
                Status = JobStatuses.Timeout,
                ErrorCode = ErrorCodes.AgentTimeout,
                DurationMs = ElapsedMs(started)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new JobResult
            {
                TraceId = request.TraceId,
                JobId = jobId,
                Status = JobStatuses.Cancelled,
                DurationMs = ElapsedMs(started)
            };
        }
    }

    private static long ElapsedMs(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
