using PaqAgent.Comprobantes;
using PaqAgent.Options;
using PaqAgent.Pedidos;
using PaqAgent.Tango;
using PaqContracts;

namespace PaqAgent.Tests;

public class PedidosPendientesRunnerTests
{
    [Fact]
    public async Task RunAsync_okRows_returnsSuccessList()
    {
        var runner = new PedidosPendientesRunner(new FakePedidosExecutor((_, _, _) =>
            new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase) { ["nroPedido"] = "P-1", ["codigoCliente"] = "001" }
            }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigoCliente"] = "001",
                ["limit"] = 10,
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var list = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(outcome.Data);
        Assert.Single(list);
    }

    [Fact]
    public async Task RunAsync_missingDatabase_returnsInvalidParameters()
    {
        var runner = new PedidosPendientesRunner(new FakePedidosExecutor((_, _, _) => throw new InvalidOperationException("no call")));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["codigoCliente"] = "001" },
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

    private sealed class FakePedidosExecutor : IPedidosPendientesSpExecutor
    {
        private readonly Func<string, string, int, IReadOnlyList<Dictionary<string, object?>>> factory;

        public FakePedidosExecutor(Func<string, string, int, IReadOnlyList<Dictionary<string, object?>>> factory) =>
            this.factory = factory;

        public Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
            string connectionString,
            string codigoCliente,
            int limit,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, codigoCliente, limit));
    }
}

public class ComprobantesRecientesRunnerTests
{
    [Fact]
    public async Task RunAsync_okRows_returnsSuccessList()
    {
        var runner = new ComprobantesRecientesRunner(new FakeComprobantesExecutor((_, _, dias, limit) =>
        {
            Assert.Equal(15, dias);
            Assert.Equal(5, limit);
            return new List<Dictionary<string, object?>>
            {
                new(StringComparer.OrdinalIgnoreCase) { ["nroComprobante"] = "A-1" }
            };
        }));

        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["codigoCliente"] = "001",
                ["dias"] = 15,
                ["limit"] = 5,
                ["_database"] = "EMPRESA_01"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var list = Assert.IsAssignableFrom<IReadOnlyList<Dictionary<string, object?>>>(outcome.Data);
        Assert.Single(list);
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

    private sealed class FakeComprobantesExecutor : IComprobantesRecientesSpExecutor
    {
        private readonly Func<string, string, int, int, IReadOnlyList<Dictionary<string, object?>>> factory;

        public FakeComprobantesExecutor(Func<string, string, int, int, IReadOnlyList<Dictionary<string, object?>>> factory) =>
            this.factory = factory;

        public Task<IReadOnlyList<Dictionary<string, object?>>> ExecuteAsync(
            string connectionString,
            string codigoCliente,
            int dias,
            int limit,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(connectionString, codigoCliente, dias, limit));
    }
}

public class TangoVersionRunnerTests
{
    [Fact]
    public async Task RunAsync_missingLlave_returnsInvalidParameters()
    {
        var runner = new TangoVersionRunner(new FakeRegistry(_ => throw new InvalidOperationException("no call")));

        var outcome = await runner.RunAsync(new Dictionary<string, object?>(), CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_notFound_returnsNotFound()
    {
        var runner = new TangoVersionRunner(new FakeRegistry(_ => null));

        var outcome = await runner.RunAsync(
            new Dictionary<string, object?> { ["llave"] = "000205/012" },
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("NOT_FOUND", outcome.ErrorCode);
    }

    [Fact]
    public async Task RunAsync_ok_returnsVersionAndSystemDir()
    {
        var runner = new TangoVersionRunner(new FakeRegistry(_ => new TangoVersionInfo
        {
            Version = "21.01.000",
            SystemDir = @"C:\Tango\Gestion\"
        }));

        var outcome = await runner.RunAsync(
            new Dictionary<string, object?> { ["llave"] = "000205/012" },
            CancellationToken.None);

        Assert.Equal(JobStatuses.Success, outcome.Status);
        var data = Assert.IsType<Dictionary<string, object?>>(outcome.Data);
        Assert.Equal("21.01.000", data["version"]);
        Assert.Equal(@"C:\Tango\Gestion\", data["systemDir"]);
    }

    private sealed class FakeRegistry : ITangoVersionRegistryReader
    {
        private readonly Func<string, TangoVersionInfo?> factory;

        public FakeRegistry(Func<string, TangoVersionInfo?> factory) => this.factory = factory;

        public TangoVersionInfo? Read(string llave) => factory(llave);
    }
}
