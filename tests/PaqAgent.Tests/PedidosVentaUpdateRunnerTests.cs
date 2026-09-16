using PaqAgent.Informes;
using PaqAgent.Options;
using PaqAgent.PedidosVenta;
using PaqContracts;

namespace PaqAgent.Tests;

public class PedidosVentaUpdateRunnerTests
{
    [Fact]
    public void Catalog_containsUpdate()
    {
        Assert.Equal(4, PedidosVentaCatalog.Operations.Count);
        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Update", out var def));
        Assert.Equal(PedidosVentaResponseShape.Update, def.Shape);
        Assert.Contains("row_version", def.Parameters);
    }

    [Fact]
    public async Task Update_missingParams_returnsInvalidParameters()
    {
        var runner = new PedidosVentaUpdateRunner(new PedidosVentaGatewayRunner(UnusedExecutor()));
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["talon_ped"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Update_sqlNotConfigured_returnsDegraded()
    {
        var runner = new PedidosVentaUpdateRunner(new PedidosVentaGatewayRunner(UnusedExecutor()));
        var outcome = await runner.RunAsync(
            new AgentOptions
            {
                AgentId = "a",
                ClientId = "c",
                AgentToken = "t",
                GatewayUrl = "http://127.0.0.1:5100/agent-hub",
                Sql = new SqlOptions()
            },
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_ped"] = 1,
                ["nro_pedido"] = "00000001"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    private static IInformesSpExecutor UnusedExecutor() =>
        new ThrowExecutor();

    private sealed class ThrowExecutor : IInformesSpExecutor
    {
        public Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
            string connectionString,
            string storedProcedure,
            IReadOnlyDictionary<string, object?> parameters,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Get no debe invocarse en este caso de prueba.");
    }

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "a",
            ClientId = "c",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions { Server = "localhost", Database = "dic", User = "sa", Password = "x" }
        };
}
