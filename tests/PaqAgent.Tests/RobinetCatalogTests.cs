using PaqAgent.Informes;
using PaqAgent.Options;
using PaqAgent.Robinet;
using PaqContracts;

namespace PaqAgent.Tests;

public class RobinetCatalogTests
{
    [Fact]
    public void Catalog_containsThreeRobinetOps()
    {
        Assert.Equal(3, RobinetCatalog.Operations.Count);
        Assert.True(RobinetCatalog.TryGet("robinet.deudas", out var deudas));
        Assert.Equal("dbo.PAQ_Robinet_Deudas", deudas.StoredProcedure);
        Assert.True(RobinetCatalog.TryGet("robinet.pedidos", out var pedidos));
        Assert.Equal("dbo.PAQ_Robinet_Pedidos", pedidos.StoredProcedure);
        Assert.True(RobinetCatalog.TryGet("robinet.cobranzas", out var cobranzas));
        Assert.Equal("dbo.PAQ_Robinet_Cobranzas", cobranzas.StoredProcedure);
    }

    [Fact]
    public async Task RunAsync_deudas_dualResultSets_returnsFilasPayload()
    {
        var runner = new InformesGatewayRunner(new FakeExecutor((_, sp, spParams) =>
        {
            Assert.Equal("dbo.PAQ_Robinet_Deudas", sp);
            Assert.Equal(1, spParams["page"]);
            Assert.Equal(200, spParams["page_size"]);
            return new List<IReadOnlyList<Dictionary<string, object?>>>
            {
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["total_filas"] = 2,
                        ["total_general"] = 150.25m
                    }
                },
                new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cod_client"] = "C001",
                        ["saldo_pendiente"] = 100m
                    },
                    new(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cod_client"] = "C002",
                        ["saldo_pendiente"] = 50.25m
                    }
                }
            };
        }));

        Assert.True(RobinetCatalog.TryGet("robinet.deudas", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMPRESA_01",
                ["empresa"] = "1",
                ["prefijo_acopio"] = null,
                ["page"] = 1,
                ["page_size"] = 200
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(2, data["total_filas"]);
        Assert.Equal(150.25m, data["total_general"]);
        var filas = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(data["filas"]);
        Assert.Equal(2, filas.Count);
    }

    [Fact]
    public async Task RunAsync_pedidos_missingDatabase_returnsInvalidParameters()
    {
        var runner = new InformesGatewayRunner(new FakeExecutor((_, _, _) =>
            throw new InvalidOperationException("no call")));
        Assert.True(RobinetCatalog.TryGet("robinet.pedidos", out var def));

        var outcome = await runner.RunAsync(
            def,
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["fecha_desde"] = "2026-01-01",
                ["fecha_hasta"] = "2026-01-31"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
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
