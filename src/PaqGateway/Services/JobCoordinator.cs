using System.Collections.Concurrent;
using PaqContracts;

namespace PaqGateway.Services;

public interface IJobCoordinator
{
    string CreateJobId();
    TaskCompletionSource<JobResult> RegisterPending(string jobId);
    bool TryComplete(JobResult result);
    void CancelAllPending();
}

public sealed class JobCoordinator : IJobCoordinator
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JobResult>> pending = new(StringComparer.Ordinal);

    public string CreateJobId() => Guid.NewGuid().ToString("N");

    public TaskCompletionSource<JobResult> RegisterPending(string jobId)
    {
        var tcs = new TaskCompletionSource<JobResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(jobId, tcs))
        {
            throw new InvalidOperationException($"Duplicate jobId {jobId}");
        }

        return tcs;
    }

    public bool TryComplete(JobResult result)
    {
        if (string.IsNullOrWhiteSpace(result.JobId))
        {
            return false;
        }

        if (!pending.TryRemove(result.JobId, out var tcs))
        {
            return false;
        }

        return tcs.TrySetResult(result);
    }

    public void CancelAllPending()
    {
        foreach (var pair in pending)
        {
            if (pending.TryRemove(pair.Key, out var tcs))
            {
                tcs.TrySetResult(new JobResult
                {
                    JobId = pair.Key,
                    Status = JobStatuses.Cancelled,
                    ErrorMessage = "Gateway shutting down"
                });
            }
        }
    }
}
