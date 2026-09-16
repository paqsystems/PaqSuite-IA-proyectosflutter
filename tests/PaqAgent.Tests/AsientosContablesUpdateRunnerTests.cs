using PaqAgent.AsientosContables;
using PaqAgent.Informes;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AsientosContablesUpdateRunnerTests
{
    [Fact]
    public void Catalog_containsUpdate()
    {
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Update", out var def));
        Assert.Equal(AsientosContablesResponseShape.Update, def.Shape);
        Assert.Equal("(orchestrated)", def.StoredProcedure);
        Assert.Contains("row_version", def.Parameters);
        Assert.Contains("renglones_json", def.Parameters);
        Assert.Contains("estado_asiento_analitico", def.Parameters);
    }

    [Fact]
    public async Task Update_missingParams_returnsInvalidParameters()
    {
        var runner = new AsientosContablesUpdateRunner(new AsientosContablesGatewayRunner(
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
    public async Task Update_missingRowVersion_returnsInvalidParameters()
    {
        var runner = new AsientosContablesUpdateRunner(new AsientosContablesGatewayRunner(
            new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call"))));
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["nro_interno_analitico"] = 10
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
        Assert.Contains("row_version", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_invalidRenglonesJson_returnsInvalidParameters()
    {
        var runner = new AsientosContablesUpdateRunner(new AsientosContablesGatewayRunner(
            new FakeExecutor((_, _, _) => throw new InvalidOperationException("no call"))));
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["nro_interno_analitico"] = 10,
                ["row_version"] = "AAAA",
                ["renglones_json"] = "{not-json"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
        Assert.Contains("renglones_json", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_sqlNotConfigured_returnsDegraded()
    {
        var runner = new AsientosContablesUpdateRunner(new AsientosContablesGatewayRunner(
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
                ["row_version"] = "AAAA",
                ["leyenda"] = "x"
            },
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
            string database,
            string procedure,
            IReadOnlyDictionary<string, object?> parameters,
            int timeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(factory(database, procedure, parameters));
    }
}
