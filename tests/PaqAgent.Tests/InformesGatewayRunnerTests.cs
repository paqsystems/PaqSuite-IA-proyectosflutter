using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class InformesGatewayRunnerTests
{
    [Fact]
    public void Catalog_containsAllSixteenInformesOps()
    {
        Assert.Equal(16, InformesCatalog.Operations.Count);
        Assert.True(InformesCatalog.TryGet("informes.ventas-listado-saldos", out var def));
        Assert.Equal("dbo.PAQ_Ventas_ListadoSaldos", def.StoredProcedure);
    }

    [Fact]
    public async Task RunAsync_missingDatabase_returnsInvalidParameters()
    {
        var runner = new InformesGatewayRunner(new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call")));
        Assert.True(InformesCatalog.TryGet("informes.ventas-listado-saldos", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["fecha_referencia"] = "2026-01-01" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_dualResultSets_returnsFilasPayload()
    {
        var runner = new InformesGatewayRunner(new FakeExecutor((_, _, spParams) =>
        {
            Assert.Equal(true, spParams["ignorar_saldo_cero"]);
            Assert.Equal(2, spParams["page"]);
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["total_filas"] = 1,
                        ["total_general"] = 10.5m
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cod_client"] = "001",
                        ["saldo"] = 10.5m
                    }
                }
            };
        }));

        Assert.True(InformesCatalog.TryGet("informes.ventas-listado-saldos", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMPRESA_01",
                ["fecha_referencia"] = "2026-01-01",
                ["ignorar_saldo_cero"] = true,
                ["page"] = 2,
                ["page_size"] = 50
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(1, data["total_filas"]);
        Assert.Equal(10.5m, data["total_general"]);
        Assert.Equal(2, data["page"]);
        var filas = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(data["filas"]);
        Assert.Single(filas);
    }

    [Fact]
    public async Task RunAsync_withoutSql_returnsDegraded()
    {
        var runner = new InformesGatewayRunner(new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call")));
        Assert.True(InformesCatalog.TryGet("informes.stock-movimiento", out var def));

        var outcome = await runner.RunAsync(
            def,
            new AgentOptions
            {
                AgentId = "a",
                ClientId = "c",
                AgentToken = "t",
                GatewayUrl = "http://127.0.0.1:5100/agent-hub"
            },
            new Dictionary<string, object?> { ["_database"] = "EMP" },
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
