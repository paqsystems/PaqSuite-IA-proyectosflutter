using PaqAgent.Options;
using PaqAgent.OrdenesCompra;
using PaqContracts;

namespace PaqAgent.Tests;

public class OrdenesCompraCreateRunnerTests
{
    [Fact]
    public void Catalog_containsGetAndCreate()
    {
        Assert.Equal(4, OrdenesCompraCatalog.Operations.Count);
        Assert.True(OrdenesCompraCatalog.TryGet("OrdenesCompra.Create", out var def));
        Assert.Equal(OrdenesCompraResponseShape.Create, def.Shape);
        Assert.Contains("renglones_json", def.Parameters);
    }

    [Fact]
    public async Task Create_missingParams_returnsInvalidParameters()
    {
        var runner = new OrdenesCompraCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?> { ["talon_oc"] = 1 },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("INVALID_PARAMETERS", outcome.ErrorCode);
    }

    [Fact]
    public async Task Create_ocasional_rejected()
    {
        var runner = new OrdenesCompraCreateRunner();
        var outcome = await runner.RunAsync(
            LabOptionsWithSql(),
            new Dictionary<string, object?>
            {
                ["_database"] = "EMP",
                ["talon_oc"] = 1,
                ["cod_provee"] = "000000",
                ["cod_lista"] = 1,
                ["renglones_json"] = """[{"codArticu":"A","cantPedida":1,"precio":10,"codDeposi":"01"}]"""
            },
            30,
            CancellationToken.None);

        Assert.Equal(JobStatuses.Failed, outcome.Status);
        Assert.Equal("OCASIONAL_NO_SOPORTADO", outcome.ErrorCode);
    }

    [Fact]
    public async Task Create_sqlNotConfigured_returnsDegraded()
    {
        var runner = new OrdenesCompraCreateRunner();
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
                ["talon_oc"] = 1,
                ["cod_provee"] = "P001",
                ["cod_lista"] = 1,
                ["renglones_json"] = """[{"codArticu":"A","cantPedida":1,"precio":10,"codDeposi":"01"}]"""
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
}
