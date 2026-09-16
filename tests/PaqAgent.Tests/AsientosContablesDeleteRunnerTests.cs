using PaqAgent.AsientosContables;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class AsientosContablesDeleteRunnerTests
{
    [Fact]
    public void Catalog_containsDelete()
    {
        Assert.True(AsientosContablesCatalog.TryGet("AsientosContables.Delete", out var def));
        Assert.Equal(AsientosContablesResponseShape.Delete, def.Shape);
        Assert.Equal("(orchestrated)", def.StoredProcedure);
        Assert.Contains("row_version", def.Parameters);
        Assert.Contains("nro_interno_analitico", def.Parameters);
    }

    [Fact]
    public void Catalog_operationsCountIncludesUpdateAndDelete()
    {
        Assert.Equal(4, AsientosContablesCatalog.Operations.Count);
    }

    [Fact]
    public async Task Delete_missingParams_returnsInvalidParameters()
    {
        var runner = new AsientosContablesDeleteRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["nro_interno_analitico"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Delete_missingRowVersion_returnsInvalidParameters()
    {
        var runner = new AsientosContablesDeleteRunner();
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
    public async Task Delete_sqlNotConfigured_returnsDegraded()
    {
        var runner = new AsientosContablesDeleteRunner();
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
                ["row_version"] = "AAAA"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public void RowVersionMatches_base64()
    {
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 7, 209 };
        var b64 = Convert.ToBase64String(bytes);
        Assert.True(AsientosContablesDeleteRunner.RowVersionMatches(b64, bytes));
        Assert.False(AsientosContablesDeleteRunner.RowVersionMatches("AAAA", bytes));
        Assert.False(AsientosContablesDeleteRunner.RowVersionMatches(null, bytes));
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
}
