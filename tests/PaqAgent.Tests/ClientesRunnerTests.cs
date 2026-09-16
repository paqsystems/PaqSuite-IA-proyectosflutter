using PaqAgent.Clientes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class ClientesBuscarRunnerTests
{
    [Fact]
    public async Task RunAsync_withoutSqlConfig_returnsDegraded()
    {
        var runner = new ClientesBuscarRunner(new FakeBuscarExecutor((_, _, _) => throw new InvalidOperationException("no call")));
        var options = new AgentOptions
        {
            AgentId = "lab-agent-01",
            ClientId = "lab",
            AgentToken = "t",
            GatewayUrl = "http://127.0.0.1:5100/agent-hub"
        };

        var outcome = await runner.RunAsync(
            options,
            new Dictionary<string, object?>
            {
                ["texto"] = "TEC",
                ["limit"] = 10,
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_missingDatabase_returnsInvalidParameters()
    {
        var runner = new ClientesBuscarRunner(new FakeBuscarExecutor((_, _, _) => throw new InvalidOperationException("no call")));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["texto"] = "TEC" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_okRows_returnsSuccessList()
    {
        var runner = new ClientesBuscarRunner(new FakeBuscarExecutor((_, _, _) =>
            new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["codigo"] = "001",
                    ["razonSocial"] = "Tec SA"
                }
            }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["texto"] = "TEC",
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var list = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(outcome.Data);
        Assert.Single(list);
        Assert.Equal("001", list[0]["codigo"]);
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

    private sealed class FakeBuscarExecutor : IClientesBuscarSpExecutor
    {
        private readonly Func<string, string, int, IReadOnlyList<Dictionary<string, object?>>> factory;

        public FakeBuscarExecutor(Func<string, string, int, IReadOnlyList<Dictionary<string, object?>>> factory)
        {
            this.factory = factory;
        }

        public Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
            string connectionString,
            string texto,
            int limit,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, texto, limit));
    }
}

public class ClientesObtenerRunnerTests
{
    [Fact]
    public async Task RunAsync_notFound_returnsNotFound()
    {
        var runner = new ClientesObtenerRunner(new FakeObtenerExecutor((_, _) => null));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigo"] = "ZZZ",
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
        var runner = new ClientesObtenerRunner(new FakeObtenerExecutor((_, _) =>
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["codigo"] = "001",
                ["razonSocial"] = "Tec SA"
            }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigo"] = "001",
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var row = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal("Tec SA", row["razonSocial"]);
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

    private sealed class FakeObtenerExecutor : IClientesObtenerSpExecutor
    {
        private readonly Func<string, string, Dictionary<string, object?>?> factory;

        public FakeObtenerExecutor(Func<string, string, Dictionary<string, object?>?> factory)
        {
            this.factory = factory;
        }

        public Task<Dictionary<string, object?>?> ExecuteAsync(
            string connectionString,
            string codigo,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, codigo));
    }
}
