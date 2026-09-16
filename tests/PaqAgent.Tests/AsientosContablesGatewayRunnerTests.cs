using PaqAgent.AsientosContables;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AsientosContablesGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsGetOp()
    {
        Assert.Contains("AsientosContables.Get", AsientosContablesCatalog.Operations);
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Get", out var def));
        Assert.Equal("dbo.PAQ_AsientosContables_Get", def.StoredProcedure);
        Assert.Equal(AsientosContablesResponseShape.Get, def.Shape);
        Assert.Contains("nro_interno_analitico", def.Parameters);
    }

    [Fact]
    public async Task RunAsync_missingParams_returnsInvalidParameters()
    {
        var runner = new AsientosContablesGatewayRunner(new FakeExecutor((_, _, _) =>
            throw new InvalidOperationException("no call")));
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["nro_interno_analitico"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_sqlNotConfigured_returnsDegraded()
    {
        var runner = new AsientosContablesGatewayRunner(new FakeExecutor((_, _, _) =>
            throw new InvalidOperationException("no call")));
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Get", out var def));

        var outcome = await runner.RunAsync(
            def,
            new AgentOptions
            {
                AgentId = "a",
                ClientId = "c",
                AgentToken = "t",
                GatewayUrl = "http://127.0.0.1:5100/agent-hub"
            },
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["nro_interno_analitico"] = 10
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_emptyCabecera_returnsNotFound()
    {
        var runner = new AsientosContablesGatewayRunner(new FakeExecutor((_, _, _) =>
            new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>(),
                Array.Empty<Dictionary<string, object?>>()
            }));

        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Get", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["nro_interno_analitico"] = 99
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_mapsDetalleConTiposAuxiliarYRowVersion()
    {
        var runner = new AsientosContablesGatewayRunner(new FakeExecutor((_, sp, spParams) =>
        {
            Assert.Equal("dbo.PAQ_AsientosContables_Get", sp);
            Assert.Equal(42, spParams["nro_interno_analitico"]);
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["idAsientoAnaliticoCn"] = 7,
                        ["nroInternoAnalitico"] = 42,
                        ["nroAsiento"] = 1001d,
                        ["fecha"] = "2026-09-14",
                        ["codTipoAsiento"] = "MAN",
                        ["codMoneda"] = "PES",
                        ["leyenda"] = "Asiento prueba",
                        ["observaciones"] = "obs",
                        ["estadoAsientoAnalitico"] = "Registrado",
                        ["estadoResumen"] = "Pendiente",
                        ["rowVersion"] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["idRenglonImporteAnaliticoCn"] = 501,
                        ["renglon"] = 1,
                        ["codCuenta"] = "11101",
                        ["dH"] = "D",
                        ["importe"] = 150.5d,
                        ["leyenda"] = "Debe"
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["idRenglonImporteAnaliticoCn"] = 501,
                        ["idAuxiliarAnaliticoCn"] = 9001,
                        ["codTipoAuxiliar"] = "CC",
                        ["codAuxiliar"] = "AUX1",
                        ["importe"] = 150.5d,
                        ["porcentaje"] = 100d
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["idAuxiliarAnaliticoCn"] = 9001,
                        ["codSubauxiliar"] = "SUB1",
                        ["importe"] = 150.5d,
                        ["porcentaje"] = 100d
                    }
                }
            };
        }));

        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Get", out var def));
        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["nro_interno_analitico"] = 42
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(7, data["idAsientoAnaliticoCn"]);
        Assert.Equal(42, data["nroInternoAnalitico"]);
        Assert.Equal(1001d, data["nroAsiento"]);
        Assert.Equal("MAN", data["codTipoAsiento"]);
        Assert.Equal(
            Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }),
            data["rowVersion"]);

        var renglones = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(data["renglones"]).ToList();
        Assert.Single(renglones);
        Assert.Equal("11101", renglones[0]["codCuenta"]);
        var tipos = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(renglones[0]["tiposAuxiliar"]).ToList();
        Assert.Single(tipos);
        Assert.Equal("CC", tipos[0]["codTipoAuxiliar"]);
        var auxiliares = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(tipos[0]["auxiliares"]).ToList();
        Assert.Equal("AUX1", auxiliares[0]["codAuxiliar"]);
        var subs = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(auxiliares[0]["subauxiliares"]).ToList();
        Assert.Equal("SUB1", subs[0]["codSubauxiliar"]);
    }

    [Fact]
    public void EncodeRowVersion_base64FromBytes()
    {
        var bytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0A };
        Assert.Equal(Convert.ToBase64String(bytes), AsientosContablesGatewayRunner.EncodeRowVersion(bytes));
        Assert.Null(AsientosContablesGatewayRunner.EncodeRowVersion(null));
        Assert.Null(AsientosContablesGatewayRunner.EncodeRowVersion(Array.Empty<byte>()));
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
