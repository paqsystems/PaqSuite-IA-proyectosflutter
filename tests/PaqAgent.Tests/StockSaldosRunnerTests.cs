using PaqAgent.Options;
using PaqAgent.Saldos;
using PaqAgent.Stock;
using PaqContracts;

namespace PaqAgent.Tests;

public class StockConsultarRunnerTests
{
    [Fact]
    public async Task RunAsync_missingDatabase_returnsInvalidParameters()
    {
        var runner = new StockConsultarRunner(new FakeStockExecutor((_, _, _) => throw new InvalidOperationException("no call")));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["codigoArticulo"] = "ART01" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_okRows_returnsSuccessList()
    {
        var runner = new StockConsultarRunner(new FakeStockExecutor((_, _, deposito) =>
        {
            Assert.Null(deposito);
            return new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["codigoArticulo"] = "ART01",
                    ["codigoDeposito"] = "01",
                    ["cantStock"] = 12.5m
                }
            };
        }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigoArticulo"] = "ART01",
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var list = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(outcome.Data);
        Assert.Single(list);
    }

    [Fact]
    public async Task RunAsync_withDeposito_passesOptionalFilter()
    {
        string? captured = "unset";
        var runner = new StockConsultarRunner(new FakeStockExecutor((_, _, deposito) =>
        {
            captured = deposito;
            return [];
        }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigoArticulo"] = "ART01",
                ["deposito"] = "DEP1",
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        Assert.Equal("DEP1", captured);
    }

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions
            {
                Server = "localhost",
                Database = "diccionario_lab",
                User = "sa",
                Password = "x"
            }
        };

    private sealed class FakeStockExecutor : IStockConsultarSpExecutor
    {
        private readonly Func<string, string, string?, IReadOnlyList<Dictionary<string, object?>>> factory;

        public FakeStockExecutor(Func<string, string, string?, IReadOnlyList<Dictionary<string, object?>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
            string connectionString,
            string codigoArticulo,
            string? deposito,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, codigoArticulo, deposito));
    }
}

public class SaldosConsultarRunnerTests
{
    [Fact]
    public async Task RunAsync_notFound_returnsNotFound()
    {
        var runner = new SaldosConsultarRunner(new FakeSaldosExecutor((_, _) => null));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigoCliente"] = "ZZZ",
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_okRow_returnsSuccess()
    {
        var runner = new SaldosConsultarRunner(new FakeSaldosExecutor((_, _) =>
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["codigoCliente"] = "001",
                ["saldoCuentaCorriente"] = 100.5m
            }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigoCliente"] = "001",
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var row = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal(100.5m, row["saldoCuentaCorriente"]);
    }

    private static AgentOptions LabOptionsWithSql() =>
        new()
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub",
            Sql = new SqlOptions
            {
                Server = "localhost",
                Database = "diccionario_lab",
                User = "sa",
                Password = "x"
            }
        };

    private sealed class FakeSaldosExecutor : ISaldosConsultarSpExecutor
    {
        private readonly Func<string, string, Dictionary<string, object?>?> factory;

        public FakeSaldosExecutor(Func<string, string, Dictionary<string, object?>?> factory)
        {
            this.factory = factory;
        }

        public Task<Dictionary<string, object?>?> ExecuteAsync(
            string connectionString,
            string codigoCliente,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, codigoCliente));
    }
}
