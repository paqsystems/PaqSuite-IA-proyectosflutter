using PaqGateway.Services;

namespace PaqGateway.Hosting;

/// <summary>Cancela jobs en vuelo al apagar (sin reentrega silenciosa).</summary>
public sealed class JobShutdownService : IHostedService
{
    private readonly IJobCoordinator jobCoordinator;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<JobShutdownService> logger;

    public JobShutdownService(
        IJobCoordinator jobCoordinator,
        IHostApplicationLifetime lifetime,
        ILogger<JobShutdownService> logger)
    {
        this.jobCoordinator = jobCoordinator;
        this.lifetime = lifetime;
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifetime.ApplicationStopping.Register(() =>
        {
            logger.LogInformation("Cancelling in-flight jobs on shutdown");
            jobCoordinator.CancelAllPending();
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
