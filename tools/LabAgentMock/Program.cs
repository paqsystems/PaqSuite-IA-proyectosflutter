using Microsoft.AspNetCore.SignalR.Client;
using PaqContracts;

// Mini cliente SignalR para lab TR-002 (no es PaqAgent / TR-005).
// Uso:
//   1) Terminal A: dotnet run --project src/PaqGateway
//   2) Terminal B: dotnet run --project tools/LabAgentMock
//   3) Terminal C: Invoke-RestMethod status / jobs/send con X-Paq-Internal-Api-Key

var hubBase = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:5100/agent-hub";
var agentId = args.ElementAtOrDefault(1) ?? "lab-agent-01";
var clientId = args.ElementAtOrDefault(2) ?? "lab";
var agentToken = args.ElementAtOrDefault(3) ?? "lab-token-manual";

var hubUrl =
    $"{hubBase}?agentId={Uri.EscapeDataString(agentId)}&clientId={Uri.EscapeDataString(clientId)}&agentToken={Uri.EscapeDataString(agentToken)}";

Console.WriteLine($"Conectando a {hubUrl}");

await using var connection = new HubConnectionBuilder()
    .WithUrl(hubUrl)
    .WithAutomaticReconnect()
    .Build();

connection.On<JobRequest>(HubMethodNames.ExecuteJob, async request =>
{
    Console.WriteLine($"ExecuteJob recibido operation={request.Operation} jobId={request.JobId} traceId={request.TraceId}");
    await connection.InvokeAsync(
        HubMethodNames.CompleteJob,
        new JobResult
        {
            TraceId = request.TraceId,
            JobId = request.JobId,
            Status = JobStatuses.Success,
            Data = new { mock = true, operation = request.Operation },
            DurationMs = 5
        });
    Console.WriteLine("CompleteJob enviado (success)");
});

connection.Reconnected += connectionId =>
{
    Console.WriteLine($"Reconectado connectionId={connectionId}");
    return Task.CompletedTask;
};

connection.Closed += error =>
{
    Console.WriteLine(error is null ? "Conexión cerrada" : $"Conexión cerrada: {error.Message}");
    return Task.CompletedTask;
};

await connection.StartAsync();
Console.WriteLine($"Conectado. agentId={agentId}. Ctrl+C para salir.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

while (!cts.IsCancellationRequested)
{
    try
    {
        await connection.InvokeAsync(
            HubMethodNames.Heartbeat,
            new AgentHeartbeat
            {
                AgentId = agentId,
                ClientId = clientId,
                AgentVersion = "lab-mock",
                Readiness = "operational",
                TimestampUtc = DateTimeOffset.UtcNow
            },
            cts.Token);
        Console.WriteLine($"Heartbeat OK {DateTimeOffset.Now:HH:mm:ss}");
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Heartbeat error: {ex.Message}");
    }

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(AgentDefaults.HeartbeatSeconds), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

Console.WriteLine("Listo.");
