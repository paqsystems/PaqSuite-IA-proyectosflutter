using PaqAgent.Informes;
using PaqAgent.Options;
using PaqAgent.OrdenesCompra;
using PaqContracts;

namespace PaqAgent.Tests;

public class OrdenesCompraGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsGetOp()
    {
        Assert.Equal(4, OrdenesCompraCatalog.Operations.Count);
        Assert.True(OrdenesCompraCatalog.TryGet("OrdenesCompra.Get", out var def));
        Assert.Equal("dbo.PAQ_OrdenesCompra_Get", def.StoredProcedure);
        Assert.Equal(OrdenesCompraResponseShape.Get, def.Shape);
    }

    [Fact]
    public async Task RunAsync_missingParams_returnsInvalidParameters()
    {
        var runner = new OrdenesCompraGatewayRunner(new FakeExecutor((_, _, _) =>
            throw new InvalidOperationException("no call")));
        Assert.True(OrdenesCompraCatalog.TryGet("OrdenesCompra.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["talon_oc"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_emptyCabecera_returnsNotFound()
    {
        var runner = new OrdenesCompraGatewayRunner(new FakeExecutor((_, _, _) =>
            new IReadOnlyList<Dictionary<string, object?>>[]
            {
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>()
            }));
        Assert.True(OrdenesCompraCatalog.TryGet("OrdenesCompra.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_oc"] = 1,
                ["n_orden_co"] = "00000001"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_mapsCabeceraRenglonesPlanesYTextos()
    {
        var runner = new OrdenesCompraGatewayRunner(new FakeExecutor((_, _, _) =>
            new IReadOnlyList<Dictionary<string, object?>>[]
            {
                new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["idCpa35"] = 10,
                        ["talonOc"] = 2,
                        ["nOrdenCo"] = "OC-1",
                        ["estado"] = 1,
                        ["totalCte"] = 100d,
                        ["totalExt"] = 0d,
                        ["rowVersion"] = new byte[] { 1, 2, 3 },
                        ["codProvee"] = "P001",
                        ["fechaEmisio"] = "2026-09-14",
                        ["leyenda1"] = "L1",
                        ["observaciones"] = "obs"
                    }
                },
                new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["idCpa36"] = 20,
                        ["nRenglonOc"] = 1,
                        ["codArticu"] = "ART",
                        ["cantPedida"] = 5d,
                        ["precio"] = 10d,
                        ["descripcionCpa36"] = "fallback"
                    }
                },
                new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["idCpa36"] = 20,
                        ["nRenglonOc"] = 1,
                        ["nPlaneOc"] = 1,
                        ["fechaRecepc"] = "2026-09-20",
                        ["cantidad"] = 5d,
                        ["cantidad2"] = 0d
                    }
                },
                new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["nRenglonOc"] = 1,
                        ["descripcion"] = "desde CPA41",
                        ["descAdicional"] = "extra"
                    }
                }
            }));
        Assert.True(OrdenesCompraCatalog.TryGet("OrdenesCompra.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_oc"] = 2,
                ["n_orden_co"] = "OC-1"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(10, data["idCpa35"]);
        Assert.Equal("P001", data["codProvee"]);
        var leyendas = Assert.IsType<Dictionary<string, object?>>(data["leyendas"]);
        Assert.Equal("L1", leyendas["leyenda1"]);
        var renglones = Assert.IsAssignableFrom<IEnumerable<object>>(data["renglones"]).Cast<Dictionary<string, object?>>().ToList();
        Assert.Single(renglones);
        Assert.Equal("desde CPA41", renglones[0]["descripcion"]);
        var planes = Assert.IsAssignableFrom<IEnumerable<object>>(renglones[0]["planesEntrega"]).Cast<Dictionary<string, object?>>().ToList();
        Assert.Single(planes);
        Assert.Equal(1, planes[0]["nPlaneOc"]);
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
