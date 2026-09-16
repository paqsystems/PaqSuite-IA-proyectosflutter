using PaqAgent.Informes;
using PaqAgent.MovimientosTesoreria;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class MovimientosTesoreriaGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsGetOp()
    {
        Assert.Equal(3, MovimientosTesoreriaCatalog.Operations.Count);
        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Get", out var def));
        Assert.Equal("dbo.PAQ_MovimientosTesoreria_Get", def.StoredProcedure);
        Assert.Equal(MovimientosTesoreriaResponseShape.Get, def.Shape);
        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Create", out _));
        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Reversion", out _));
    }

    [Fact]
    public async Task RunAsync_missingParams_returnsInvalidParameters()
    {
        var runner = new MovimientosTesoreriaGatewayRunner(new FakeExecutor((_, _, _) =>
            throw new InvalidOperationException("no call")));
        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["cod_comp"] = "REC" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_emptyCabecera_returnsNotFound()
    {
        var runner = new MovimientosTesoreriaGatewayRunner(new FakeExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>()
            }));

        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Get", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["cod_comp"] = "REC",
                ["n_comp"] = "00000001"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_mapsDetalleCompleto()
    {
        var expectedRowVersion = MovimientosTesoreriaGatewayRunner.EncodeRowVersion(
            42,
            "A",
            "2026-01-15 10:00:00",
            "10:00:00",
            7);

        var runner = new MovimientosTesoreriaGatewayRunner(new FakeExecutor((_, sp, spParams) =>
        {
            Assert.Equal("dbo.PAQ_MovimientosTesoreria_Get", sp);
            Assert.Equal("REC", spParams["cod_comp"]);
            Assert.Equal("00000001", spParams["n_comp"]);
            Assert.Equal(0, spParams["barra"]);
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["codComp"] = "REC",
                        ["nComp"] = "00000001",
                        ["barra"] = 0,
                        ["nInterno"] = 7,
                        ["idSba04"] = 42,
                        ["clase"] = 1,
                        ["situacion"] = "A",
                        ["externo"] = false,
                        ["fecha"] = "2026-01-15",
                        ["concepto"] = "Cobro",
                        ["cotizacion"] = 1d,
                        ["codClient"] = "000001",
                        ["codProvee"] = null,
                        ["observaciones"] = "ok",
                        ["fechaUltimaModificacion"] = "2026-01-15 10:00:00",
                        ["horaUltimaModificacion"] = "10:00:00"
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["renglon"] = 1,
                        ["codCta"] = 100,
                        ["dH"] = "D",
                        ["monto"] = 50d,
                        ["leyenda"] = "linea",
                        ["idSba05"] = 9
                    }
                },
                Array.Empty<Dictionary<string, object?>>()
            };
        }));

        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Get", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["cod_comp"] = "rec",
                ["n_comp"] = "00000001"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal("REC", data["codComp"]);
        Assert.Equal(42, data["idSba04"]);
        Assert.Equal(expectedRowVersion, data["rowVersion"]);
        Assert.False(data.ContainsKey("reversion"));
        var renglones = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(data["renglones"]).ToList();
        Assert.Single(renglones);
        Assert.Equal(100, renglones[0]["codCta"]);
        Assert.Equal("D", renglones[0]["dH"]);
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
