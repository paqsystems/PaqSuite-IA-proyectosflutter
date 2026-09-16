using PaqAgent.Options;
using PaqAgent.PedidosVenta;
using PaqContracts;

namespace PaqAgent.Tests;

public class PedidosVentaDeleteRunnerTests
{
    [Fact]
    public void Catalog_containsDelete()
    {
        Assert.Equal(4, PedidosVentaCatalog.Operations.Count);
        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Delete", out var def));
        Assert.Equal(PedidosVentaResponseShape.Delete, def.Shape);
        Assert.Contains("row_version", def.Parameters);
    }

    [Fact]
    public async Task Delete_missingParams_returnsInvalidParameters()
    {
        var runner = new PedidosVentaDeleteRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["talon_ped"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Delete_sqlNotConfigured_returnsDegraded()
    {
        var runner = new PedidosVentaDeleteRunner();
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
