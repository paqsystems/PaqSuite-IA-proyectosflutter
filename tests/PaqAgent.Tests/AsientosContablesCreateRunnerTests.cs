using PaqAgent.AsientosContables;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AsientosContablesCreateRunnerTests
{
    [Fact]
    public void Catalog_containsGetAndCreate()
    {
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Create", out var def));
        Assert.Equal(AsientosContablesResponseShape.Create, def.Shape);
        Assert.Equal("(orchestrated)", def.StoredProcedure);
        Assert.Contains("renglones_json", def.Parameters);
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Get", out _));
        Assert.Equal(4, AsientosContablesCatalog.Operations.Count);
    }

    [Fact]
    public async Task Create_missingParams_returnsInvalidParameters()
    {
        var runner = new AsientosContablesCreateRunner(new AsientosContablesGatewayRunner(
            new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call"))));
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["nro_interno_analitico"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Create_invalidRenglonesJson_returnsInvalidParameters()
    {
        var runner = new AsientosContablesCreateRunner(new AsientosContablesGatewayRunner(
            new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call"))));
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["nro_interno_analitico"] = 10,
                ["cod_tipo_asiento"] = "MAN",
                ["fecha"] = "2026-09-14",
                ["renglones_json"] = "{not-json"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
        Assert.Contains("renglones_json", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_sqlNotConfigured_returnsDegraded()
    {
        var runner = new AsientosContablesCreateRunner(new AsientosContablesGatewayRunner(
            new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call"))));
        var outcome = await runner.RunAsync(
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
                ["nro_interno_analitico"] = 10,
                ["cod_tipo_asiento"] = "MAN",
                ["fecha"] = "2026-09-14",
                ["renglones_json"] = """[{"renglon":1,"codCuenta":"111","dH":"D","importe":10},{"renglon":2,"codCuenta":"211","dH":"H","importe":10}]"""
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public void ParseRenglones_mapsCamelCase()
    {
        var list = AsientosContablesCreateRunner.ParseRenglones(
            """[{"renglon":1,"codCuenta":"11101","dH":"D","importe":100.5,"leyenda":"x","tiposAuxiliar":[{"codTipoAuxiliar":"CC","auxiliares":[{"codAuxiliar":"A1","importe":100.5,"porcentaje":100}]}]}]""");

        Assert.Single(list);
        Assert.Equal(1, list[0].Renglon);
        Assert.Equal("11101", list[0].CodCuenta);
        Assert.Equal("D", list[0].DH);
        Assert.Equal(100.5m, list[0].Importe);
        Assert.Equal("x", list[0].Leyenda);
        Assert.Single(list[0].TiposAuxiliar);
        Assert.Equal("CC", list[0].TiposAuxiliar[0].CodTipoAuxiliar);
        Assert.Equal("A1", list[0].TiposAuxiliar[0].Auxiliares[0].CodAuxiliar);
    }

    [Fact]
    public void PartidaDoble_balanceadoYDesbalanceado()
    {
        var ok = AsientosContablesCreateRunner.ParseRenglones(
            """[{"renglon":1,"codCuenta":"1","dH":"D","importe":100},{"renglon":2,"codCuenta":"2","dH":"H","importe":100}]""");
        Assert.True(AsientosContablesCreateRunner.EstaBalanceado(ok));

        var bad = AsientosContablesCreateRunner.ParseRenglones(
            """[{"renglon":1,"codCuenta":"1","dH":"D","importe":100},{"renglon":2,"codCuenta":"2","dH":"H","importe":50}]""");
        Assert.False(AsientosContablesCreateRunner.EstaBalanceado(bad));
    }

    [Fact]
    public void ExigePartidaDoble_soloIngresadoRegistrado()
    {
        Assert.True(AsientosContablesCreateRunner.ExigePartidaDoble("Ingresado"));
        Assert.True(AsientosContablesCreateRunner.ExigePartidaDoble("Registrado"));
        Assert.False(AsientosContablesCreateRunner.ExigePartidaDoble("Borrador"));
    }

    [Fact]
    public void EstadoResumenFromGenera()
    {
        Assert.Equal("Pendiente", AsientosContablesCreateRunner.EstadoResumenFromGenera("S"));
        Assert.Equal("No genera", AsientosContablesCreateRunner.EstadoResumenFromGenera("N"));
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
