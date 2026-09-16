using PaqAgent.Informes;
using PaqAgent.Options;
using PaqAgent.PedidosVenta;
using PaqContracts;

namespace PaqAgent.Tests;

public class PedidosVentaGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsGetOp()
    {
        Assert.Equal(4, PedidosVentaCatalog.Operations.Count);
        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Get", out var def));
        Assert.Equal("dbo.PAQ_PedidosVenta_Get", def.StoredProcedure);
        Assert.Equal(PedidosVentaResponseShape.Get, def.Shape);
    }

    [Fact]
    public async Task RunAsync_missingParams_returnsInvalidParameters()
    {
        var runner = new PedidosVentaGatewayRunner(new FakeExecutor((_, _, _) =>
            throw new InvalidOperationException("no call")));
        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["talon_ped"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_emptyCabecera_returnsNotFound()
    {
        var runner = new PedidosVentaGatewayRunner(new FakeExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>()
            }));

        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Get", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_ped"] = 1,
                ["nro_pedido"] = "00000001"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_mapsPedidoCompleto()
    {
        var runner = new PedidosVentaGatewayRunner(new FakeExecutor((_, sp, spParams) =>
        {
            Assert.Equal("dbo.PAQ_PedidosVenta_Get", sp);
            Assert.Equal(12, spParams["talon_ped"]);
            Assert.Equal("00000099", spParams["nro_pedido"]);
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["idGva21"] = 55,
                        ["talonPed"] = 12,
                        ["nroPedido"] = "00000099",
                        ["estado"] = 1,
                        ["totalPedi"] = 100d,
                        ["totalPediConImpuestos"] = 121d,
                        ["rowVersion"] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
                        ["codClient"] = "000001",
                        ["fechaPedi"] = "2026-01-15",
                        ["compStk"] = true,
                        ["monCte"] = true,
                        ["cotiz"] = 1d,
                        ["nLista"] = 1,
                        ["condVta"] = 2,
                        ["totalPercepciones"] = 0d
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["idGva03"] = 9,
                        ["nRenglon"] = 1,
                        ["codArticu"] = "ART1",
                        ["cantPedid"] = 2d,
                        ["precio"] = 50d
                    }
                },
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>()
            };
        }));

        Assert.True(PedidosVentaCatalog.TryGet("PedidosVenta.Get", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_ped"] = 12,
                ["nro_pedido"] = "00000099"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(55, data["idGva21"]);
        Assert.Equal(12, data["talonPed"]);
        Assert.Equal("00000099", data["nroPedido"]);
        Assert.Equal(Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }), data["rowVersion"]);
        Assert.Null(data["datosCliente"]);
        var renglones = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(data["renglones"]).ToList();
        Assert.Single(renglones);
        Assert.Equal("ART1", renglones[0]["codArticu"]);
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

    private sealed class FakeExecutor : IInformesSpExecutor
    {
        private readonly Func<string, string, IReadOnlyDictionary<string, object?>, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory;

        public FakeExecutor(
            Func<string, string, IReadOnlyDictionary<string, object?>, IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<IReadOnlyList<Dictionary<string, object?>>>> ExecuteAsync(
            string connectionString,
            string storedProcedure,
            IReadOnlyDictionary<string, object?> spParameters,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, storedProcedure, spParameters));
    }
}
