using PaqAgent.Options;

namespace PaqAgent.Partes;

/// <summary>
/// D6.10.5 — Aprobar PartesOperario Submitted→Reviewed (supervisor, sin filtro de propietario).
/// </summary>
public sealed class PartesOperarioAprobarRunner
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
            PartesOperarioSupervisorAccion.Aprobar);
}
