using PaqAgent.MovimientosTesoreria;
using PaqAgent.Options;
using PaqContracts;

namespace PaqAgent.Tests;

public class MovimientosTesoreriaReversionRunnerTests
{
    [Fact]
    public void Catalog_containsReversion()
    {
        Assert.Equal(3, MovimientosTesoreriaCatalog.Operations.Count);
        Assert.True(MovimientosTesoreriaCatalog.TryGet("MovimientosTesoreria.Reversion", out var def));
        Assert.Equal(MovimientosTesoreriaResponseShape.Reversion, def.Shape);
        Assert.Equal("(orchestrated)", def.StoredProcedure);
        Assert.Contains("row_version", def.Parameters);
    }

    [Fact]
    public async Task Reversion_missingParams_returnsInvalidParameters()
    {
        var runner = new MovimientosTesoreriaReversionRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["cod_comp"] = "REC" },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Reversion_missingRowVersion_returnsInvalidParameters()
    {
        var runner = new MovimientosTesoreriaReversionRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["cod_comp"] = "REC",
                ["n_comp"] = " 0000000000001"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
        Assert.Contains("row_version", outcome.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reversion_sqlNotConfigured_returnsDegraded()
    {
        var runner = new MovimientosTesoreriaReversionRunner();
        var outcome = await runner.RunAsync(
            new AgentOptions
            {
                AgentId = "a",
                ClientId = "c",
                AgentToken = "t",
                GatewayUrl = "http://127.0.0.1:5100/agent-hub",
                Sql = new SqlOptions()
            },
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["cod_comp"] = "REC",
                ["n_comp"] = " 0000000000001",
                ["row_version"] = "abc"
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Degraded, outcome.Status);
        Assert.Equal("SQL_NOT_CONFIGURED", outcome.ErrorCode);
    }

    [Fact]
    public void EncodeRowVersion_stableBase64()
    {
        var a = MovimientosTesoreriaGatewayRunner.EncodeRowVersion(10, "N", "2026-01-01 00:00:00", "120000", 5);
        var b = MovimientosTesoreriaGatewayRunner.EncodeRowVersion(10, "N", "2026-01-01 00:00:00", "120000", 5);
        Assert.Equal(a, b);
        Assert.NotEqual(
            a,
            MovimientosTesoreriaGatewayRunner.EncodeRowVersion(10, "A", "2026-01-01 00:00:00", "120000", 5));
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
