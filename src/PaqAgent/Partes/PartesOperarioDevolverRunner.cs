using PaqAgent.Options;

namespace PaqAgent.Partes;

/// <summary>
/// D6.10.6 — Devolver PartesOperario Submitted→Open (supervisor; limpia revisión).
/// </summary>
public sealed class PartesOperarioDevolverRunner
{
    public Task<PartesOutcome> RunAsync(
        AgentOptions agentOptions,
        IReadOnlyDictionary<string, object?> parameters,
        int timeoutSeconds,
        CancellationToken cancellationToken) =>
        PartesOperarioSupervisorTransitions.RunAsync(
            agentOptions,
            parameters,
            timeoutSeconds,
            cancellationToken,
            PartesOperarioSupervisorAccion.Devolver);
}
