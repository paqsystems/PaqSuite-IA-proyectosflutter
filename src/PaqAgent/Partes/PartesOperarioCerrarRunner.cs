using PaqAgent.Options;

namespace PaqAgent.Partes;

/// <summary>
/// D6.10.7 — Cerrar PartesOperario Reviewed→Locked (supervisor, sin filtro de propietario).
/// </summary>
public sealed class PartesOperarioCerrarRunner
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
            PartesOperarioSupervisorAccion.Cerrar);
}
